#!/bin/sh
# Manda la contraseña SMTP al servidor y conecta el correo real (ADR-0008).
#
#   sh scripts/enviar-clave-correo.sh <remitente> [host] [puerto] [usuario]
#
# Ejemplo con Gmail, donde el usuario y el remitente son la misma dirección:
#
#   sh scripts/enviar-clave-correo.sh tucorreo@gmail.com
#
# La contraseña **se escribe al vuelo y no se ve**: no va como argumento, no queda en el historial
# de la shell y no se guarda en ningún archivo local. Viaja por la entrada estándar hasta el guion
# del servidor, que la escribe en el .env de allí.
set -eu

REMITENTE="${1:-}"
HOST="${2:-smtp.gmail.com}"
PUERTO="${3:-587}"
USUARIO="${4:-$REMITENTE}"

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"

if [ -z "$REMITENTE" ]; then
  echo "Falta el remitente. Uso: sh scripts/enviar-clave-correo.sh <remitente> [host] [puerto] [usuario]" >&2
  exit 1
fi

printf 'Contraseña SMTP (no se verá al escribir): '
stty -echo 2>/dev/null || true
read -r CLAVE
stty echo 2>/dev/null || true
printf '\n'

# Google enseña la contraseña de aplicación en cuatro grupos de cuatro separados por espacios,
# y copiarla tal cual es lo natural. Los espacios no forman parte de la clave: si se dejan, la
# autenticación falla con un error que no menciona espacios por ninguna parte.
CLAVE=$(printf '%s' "$CLAVE" | tr -d ' \t\r\n')

if [ -z "$CLAVE" ]; then
  echo "No se escribió ninguna contraseña." >&2
  exit 1
fi

echo "Clave de ${#CLAVE} caracteres. Enviando a $MAQUINA..."

printf '%s\n' "$CLAVE" | ssh -i "$LLAVE" "$MAQUINA" \
  "cd UcoKudos && git pull -q && sh scripts/conectar-correo.sh $HOST $PUERTO $USUARIO $REMITENTE"
