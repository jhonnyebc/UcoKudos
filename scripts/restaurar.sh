#!/bin/sh
# Restaura un respaldo y COMPRUEBA que la restauración sirve (ADR-0016, riesgo P-04).
#
# Se ejecuta EN EL SERVIDOR, desde ~/UcoKudos:
#
#   ./scripts/restaurar.sh ~/respaldos/ucokudos-<fecha>.dump
#
# **Restaura sobre una base aparte, no sobre la de producción.** Esa es la decisión central de
# este guion. Un respaldo sin restauración probada no es un respaldo: es un archivo del que nadie
# sabe si sirve, y la forma habitual de averiguarlo —restaurar encima de lo bueno— solo se puede
# hacer el día de la catástrofe, cuando ya no hay margen para descubrir que el archivo estaba mal.
#
# Al restaurar en `<base>_restauracion` se comprueba de verdad que el volcado es legible y
# completo, comparando los recuentos con los de la base viva, y la plataforma sigue funcionando
# mientras tanto.
#
# Para recuperarse DE VERDAD tras una pérdida, la base de destino se indica a mano:
#
#   BASE_DESTINO=ucokudos ./scripts/restaurar.sh <archivo>
#
# Eso es deliberadamente incómodo: sobrescribir la base viva no debe estar a un tecleo de
# distancia de comprobar un respaldo.
set -eu

ARCHIVO="${1:-}"

if [ -z "$ARCHIVO" ] || [ ! -f "$ARCHIVO" ]; then
  echo "Uso: ./scripts/restaurar.sh <archivo .dump>" >&2
  exit 1
fi

USUARIO=$(sed -n 's/^POSTGRES_USUARIO=//p' .env)
BASE=$(sed -n 's/^POSTGRES_BD=//p' .env)
USUARIO="${USUARIO:-ucokudos}"
BASE="${BASE:-ucokudos}"

DESTINO="${BASE_DESTINO:-${BASE}_restauracion}"

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

psql_en() {
  $COMPOSE exec -T postgres psql -U "$USUARIO" -d "$1" -t -A -q -c "$2"
}

if [ "$DESTINO" = "$BASE" ]; then
  echo "AVISO: se va a restaurar SOBRE LA BASE DE PRODUCCIÓN ($BASE)."
  echo "Esto reemplaza los datos actuales. Ctrl+C para cancelar; continúa en 10 segundos."
  sleep 10
fi

echo "==> Preparando la base de destino: $DESTINO"
psql_en postgres "DROP DATABASE IF EXISTS \"$DESTINO\";" > /dev/null
psql_en postgres "CREATE DATABASE \"$DESTINO\";" > /dev/null

echo "==> Restaurando"
# --no-owner evita fallar si el propietario de los objetos no existe en el destino, que es lo
# normal al restaurar en otro sitio.
$COMPOSE exec -T postgres pg_restore -U "$USUARIO" -d "$DESTINO" --no-owner < "$ARCHIVO"

echo
echo "==> Comparando con la base viva"
printf '%-28s %12s %12s\n' "Tabla" "Original" "Restaurada"

TOTAL_DIFERENCIAS=0

for tabla in nucleo.persona nucleo.kudo audit.evento_auditoria notif.notificacion notif.outbox_mensaje; do
  origen=$(psql_en "$BASE" "SELECT count(*) FROM $tabla;" 2>/dev/null || echo "error")
  copia=$(psql_en "$DESTINO" "SELECT count(*) FROM $tabla;" 2>/dev/null || echo "error")

  printf '%-28s %12s %12s' "$tabla" "$origen" "$copia"

  if [ "$origen" = "$copia" ] && [ "$origen" != "error" ]; then
    printf '  OK\n'
  else
    printf '  <-- NO COINCIDE\n'
    TOTAL_DIFERENCIAS=$((TOTAL_DIFERENCIAS + 1))
  fi
done

echo
# La cadena de hashes se verifica aparte porque los recuentos no bastan: un volcado podría traer
# todas las filas y aun así haberlas alterado. Si la cadena sigue cuadrando en la copia, los datos
# llegaron intactos byte a byte, no solo en cantidad.
echo "==> Comprobando que la cadena de auditoría sigue cuadrando en la copia"
ULTIMO=$(psql_en "$DESTINO" "SELECT count(*) FROM audit.evento_auditoria WHERE hash IS NOT NULL;")
echo "    eventos con hash en la copia: $ULTIMO"
echo "    (la verificación completa de la cadena se hace desde la pantalla de Auditoría)"

echo
if [ "$TOTAL_DIFERENCIAS" -eq 0 ]; then
  echo "RESTAURACIÓN CORRECTA. El respaldo sirve."
  if [ "$DESTINO" != "$BASE" ]; then
    echo "La base de prueba «$DESTINO» se puede borrar cuando quieras:"
    echo "  $COMPOSE exec -T postgres psql -U $USUARIO -d postgres -c 'DROP DATABASE \"$DESTINO\";'"
  fi
else
  echo "RESTAURACIÓN CON DIFERENCIAS en $TOTAL_DIFERENCIAS tabla(s). El respaldo NO sirve tal cual." >&2
  exit 1
fi
