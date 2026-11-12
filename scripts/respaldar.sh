#!/bin/sh
# Respaldo de la base y de los objetos (ADR-0016, riesgo P-04 del DAS).
#
# Se ejecuta EN EL SERVIDOR, desde ~/UcoKudos:
#
#   ./scripts/respaldar.sh
#
# Deja dos archivos con la misma marca de tiempo en ~/respaldos:
#
#   ucokudos-<fecha>.dump    la base, en formato comprimido de PostgreSQL
#   ucokudos-<fecha>.tar.gz  el volumen de objetos (adjuntos)
#
# **Por qué `pg_dump` y no copiar el volumen de la base.** Copiar los archivos de PostgreSQL con
# el motor encendido produce una copia inconsistente: puede quedar a medias de una escritura. El
# volcado lógico se hace dentro de una transacción y siempre representa un instante coherente.
#
# Los objetos sí se copian como archivos, porque son inmutables una vez subidos: un adjunto no se
# modifica, así que no hay escrituras a medias que capturar.
set -eu

DESTINO="${DESTINO:-$HOME/respaldos}"
MARCA=$(date -u +%Y%m%d-%H%M%S)

USUARIO=$(sed -n 's/^POSTGRES_USUARIO=//p' .env)
BASE=$(sed -n 's/^POSTGRES_BD=//p' .env)
USUARIO="${USUARIO:-ucokudos}"
BASE="${BASE:-ucokudos}"

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

mkdir -p "$DESTINO"

ARCHIVO_BASE="$DESTINO/ucokudos-$MARCA.dump"
ARCHIVO_OBJETOS="$DESTINO/ucokudos-$MARCA.tar.gz"

echo "==> Volcando la base $BASE"
# -Fc es el formato comprimido propio: permite restaurar tablas sueltas y ocupa mucho menos.
$COMPOSE exec -T postgres pg_dump -U "$USUARIO" -d "$BASE" -Fc > "$ARCHIVO_BASE"

# **El tamaño se comprueba aquí y no al final.** Una redirección como la de arriba crea el archivo
# aunque el mandato falle, así que un respaldo roto se ve exactamente igual que uno bueno: un
# archivo con su nombre y su fecha. Es el fallo más peligroso de un respaldo, porque no se
# descubre hasta el día que hace falta.
TAMANO=$(wc -c < "$ARCHIVO_BASE")
if [ "$TAMANO" -lt 1024 ]; then
  echo "El volcado ocupa $TAMANO bytes: está vacío o incompleto. Se borra." >&2
  rm -f "$ARCHIVO_BASE"
  exit 1
fi

echo "==> Copiando el volumen de objetos"

# **El nombre real del volumen lleva delante el del proyecto de Compose**, que sale del nombre de
# la carpeta. Se resuelve en lugar de escribirlo fijo: si la carpeta se llamara de otra forma,
# Docker crearía un volumen nuevo y vacío con el nombre que le pidiéramos y el respaldo
# empaquetaría nada, sin dar ningún error.
VOLUMEN=$(docker volume ls --format '{{.Name}}' | grep -- '-objetos$' | head -1)

if [ -z "$VOLUMEN" ]; then
  echo "No se encontró el volumen de objetos. Se borra el volcado para no dejar un respaldo a medias." >&2
  rm -f "$ARCHIVO_BASE"
  exit 1
fi

echo "    volumen: $VOLUMEN"

# Se monta el volumen en un contenedor mínimo y se empaqueta desde dentro, que es la forma de
# llegar a un volumen de Docker sin depender de dónde lo guarde el motor en el disco.
docker run --rm \
  -v "$VOLUMEN":/datos:ro \
  -v "$DESTINO:/salida" \
  alpine:3.22 tar czf "/salida/ucokudos-$MARCA.tar.gz" -C /datos .

echo
echo "Respaldo terminado:"
ls -lh "$ARCHIVO_BASE" "$ARCHIVO_OBJETOS"

echo
echo "Para comprobar que se puede restaurar:"
echo "  ./scripts/restaurar.sh $ARCHIVO_BASE"
