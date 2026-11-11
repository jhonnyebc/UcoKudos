#!/bin/sh
# Enseña el estado real de los avisos en el servidor: qué se encoló y qué se intentó enviar.
#
#   sh scripts/ver-envios.sh
#
# Solo lee. No cambia nada.
#
# Mirar la tabla y no el registro es deliberado. El despachador consulta el Outbox cada pocos
# segundos, así que las últimas líneas del registro cubren apenas unos minutos y el procesamiento
# de un Kudo publicado hace rato ya se perdió de vista. La tabla, en cambio, guarda el resultado:
# si un envío falló, el motivo está en `ultimo_error` o en `error`, no en la pantalla.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"

ssh -i "$LLAVE" "$MAQUINA" '
  cd UcoKudos
  USUARIO=$(sed -n "s/^POSTGRES_USUARIO=//p" .env)
  BASE=$(sed -n "s/^POSTGRES_BD=//p" .env)

  consultar() {
    docker compose -f docker-compose.yml -f docker-compose.produccion.yml exec -T postgres \
      psql -U "${USUARIO:-ucokudos}" -d "${BASE:-ucokudos}" -P pager=off -c "$1"
  }

  echo "=== Outbox: lo que se encoló ==="
  consultar "SELECT tipo_evento, ocurrido_en, procesado_en, intentos, ultimo_error
             FROM notif.outbox_mensaje ORDER BY ocurrido_en DESC LIMIT 10;"

  echo "=== Notificaciones: lo que se intentó enviar por cada canal ==="
  consultar "SELECT canal, clave_mensaje, estado, enviada_en, id_externo, error
             FROM notif.notificacion ORDER BY enviada_en DESC NULLS FIRST LIMIT 15;"

  echo "=== Contacto del destinatario (sin enseñar el número entero) ==="
  consultar "SELECT nombre_completo, right(celular_e164, 4) AS ultimos4, celular_verificado
             FROM nucleo.persona
             WHERE id = '"'"'0193b000-0000-7000-8000-000000000010'"'"';"
'
