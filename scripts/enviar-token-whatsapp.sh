#!/bin/sh
# Manda el token de WhatsApp del .env local al servidor y conecta el canal real (ADR-0007).
#
# Se ejecuta desde la carpeta del repositorio, en Git Bash:
#
#   sh scripts/enviar-token-whatsapp.sh
#
# Existe por una razón práctica: la orden equivalente escrita a mano es una tubería larga con
# comillas, y pegarla falla tanto en PowerShell (que destroza el entrecomillado) como en Git Bash
# (donde Ctrl+V no pega). Guardada aquí, basta escribir una línea corta.
#
# El token **nunca** viaja como argumento ni aparece en la orden: sale del .env por una tubería
# y entra al guion remoto por la entrada estándar. Así no queda en `ps` ni en el historial de
# ninguna de las dos máquinas.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"
PHONE_NUMBER_ID="${PHONE_NUMBER_ID:-1355066904358456}"

if [ ! -f .env ]; then
  echo "No hay .env aquí. Ejecutar desde la carpeta del repositorio." >&2
  exit 1
fi

if [ ! -f "$LLAVE" ]; then
  echo "No se encuentra la llave $LLAVE." >&2
  exit 1
fi

# Se comprueba que hay token ANTES de abrir la conexión. Sin esto, una tubería vacía llegaría al
# servidor, el guion remoto se quejaría de que no recibió nada y el error apuntaría al sitio
# equivocado: parecería un problema del servidor cuando el .env local es el que está incompleto.
TOKEN=$(sed -n 's/^WHATSAPP_ACCESS_TOKEN=//p' .env)
if [ -z "$TOKEN" ]; then
  echo "El .env local no tiene WHATSAPP_ACCESS_TOKEN con valor." >&2
  exit 1
fi

echo "Token local: ${#TOKEN} caracteres. Enviando a $MAQUINA..."

printf '%s\n' "$TOKEN" | ssh -i "$LLAVE" "$MAQUINA" \
  "cd UcoKudos && git pull -q && sh scripts/conectar-whatsapp.sh $PHONE_NUMBER_ID"
