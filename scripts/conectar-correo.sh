#!/bin/sh
# Conecta un proveedor SMTP real en el servidor (ADR-0008).
#
# La contraseña llega por la **entrada estándar**, nunca como argumento:
#
#   printf '%s\n' 'LA-CLAVE' | ssh <maquina> "cd UcoKudos && sh scripts/conectar-correo.sh <host> <puerto> <usuario> <remitente>"
#
# O, más cómodo, con el guion local scripts/enviar-clave-correo.sh.
#
# Que no sea un argumento importa: los argumentos de un proceso los lee cualquiera con `ps` y
# quedan en el historial de la shell. Por la entrada estándar no quedan en ninguno de los dos.
set -eu

HOST="${1:-}"
PUERTO="${2:-}"
USUARIO="${3:-}"
REMITENTE="${4:-}"

if [ -z "$HOST" ] || [ -z "$PUERTO" ] || [ -z "$USUARIO" ] || [ -z "$REMITENTE" ]; then
  echo "Uso: sh scripts/conectar-correo.sh <host> <puerto> <usuario> <remitente>" >&2
  exit 1
fi

if [ ! -f .env ]; then
  echo "No hay .env aquí. Ejecutar desde la carpeta del repositorio en el servidor." >&2
  exit 1
fi

read -r CLAVE || true

if [ -z "${CLAVE:-}" ]; then
  echo "No llegó ninguna clave por la entrada estándar." >&2
  exit 1
fi

# Se comprueba que la clave existe antes de reemplazarla y se vuelve a leer después. `sed -i`
# sobre una línea que no está no falla: no cambia nada y termina con código 0, de modo que el
# guion seguiría, reiniciaría la API y diría LISTO con el correo todavía en simulado.
asignar() {
  clave="$1"
  valor="$2"

  if ! grep -q "^$clave=" .env; then
    echo "El .env del servidor no tiene la clave $clave. No se toca nada." >&2
    exit 1
  fi

  sed -i "s#^$clave=.*#$clave=$valor#" .env

  leido=$(sed -n "s/^$clave=//p" .env)
  if [ "$leido" != "$valor" ]; then
    echo "La clave $clave no quedó con el valor esperado. Se detiene." >&2
    exit 1
  fi
}

asignar SMTP_PROVEEDOR Smtp
asignar SMTP_HOST "$HOST"
asignar SMTP_PUERTO "$PUERTO"
asignar SMTP_TLS true
asignar SMTP_USUARIO "$USUARIO"
asignar SMTP_REMITENTE "$REMITENTE"
asignar SMTP_PASSWORD "$CLAVE"

echo "Correo: $USUARIO en $HOST:$PUERTO, remitente $REMITENTE."
echo "Clave guardada y verificada: ${#CLAVE} caracteres."

echo "==> Reiniciando la API"
docker compose -f docker-compose.yml -f docker-compose.produccion.yml up -d api

echo "==> Esperando a que responda"
intentos=0
until curl -fsS http://localhost/salud/listo > /dev/null 2>&1; do
  intentos=$((intentos + 1))
  if [ "$intentos" -ge 30 ]; then
    echo "La API no respondió a tiempo." >&2
    exit 1
  fi
  sleep 2
done

echo "LISTO"
