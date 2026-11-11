#!/bin/sh
# Enseña qué pasó con los envíos de WhatsApp en el servidor (ADR-0007, ADR-0015).
#
# Se ejecuta desde la carpeta del repositorio, en Git Bash:
#
#   sh scripts/ver-registro-whatsapp.sh
#
# Solo lee. No cambia nada en el servidor.
#
# Existe porque la orden equivalente es una tubería larga con comillas anidadas que hay que pegar
# en una terminal donde pegar es incómodo. Escrito aquí, basta una línea corta.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"
LINEAS="${LINEAS:-400}"

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

echo "=== 1. Configuración del canal (sin revelar el token) ==="
echo
echo "=== 2. Últimas líneas del registro que mencionan WhatsApp ==="
echo

ssh -i "$LLAVE" "$MAQUINA" "
  cd UcoKudos

  # Del token solo se enseña la longitud. Saber que llegó entero basta para descartarlo como
  # causa, y enseñarlo no aportaría nada que compense dejarlo en pantalla y en el historial.
  for clave in WHATSAPP_PROVEEDOR WHATSAPP_PHONE_NUMBER_ID WHATSAPP_VERSION_GRAPH; do
    printf '%s = %s\n' \"\$clave\" \"\$(sed -n \"s/^\$clave=//p\" .env)\"
  done
  token=\$(sed -n 's/^WHATSAPP_ACCESS_TOKEN=//p' .env)
  printf 'WHATSAPP_ACCESS_TOKEN = %s caracteres\n\n' \"\${#token}\"

  # Se filtra por varias palabras a la vez porque el fallo puede aparecer con cualquiera de ellas
  # según de dónde venga: del adaptador, de la respuesta de Meta o del despachador del Outbox.
  $COMPOSE logs api --tail $LINEAS 2>&1 \
    | grep -iE 'whatsapp|meta|graph|plantilla|template|outbox|despach|aviso' \
    | tail -60

  echo
  echo '--- errores recientes, por si el fallo no menciona el canal ---'
  $COMPOSE logs api --tail $LINEAS 2>&1 | grep -iE ' (err|error|fail|exception)' | tail -20
"
