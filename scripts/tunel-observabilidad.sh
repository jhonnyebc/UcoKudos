#!/bin/sh
# Abre un túnel al panel de observabilidad del servidor (ADR-0014, ADR-0016 punto 7).
#
#   sh scripts/tunel-observabilidad.sh
#
# Deja la terminal ocupada: el túnel vive mientras no se cierre con Ctrl+C.
# Luego el panel está en http://localhost:18890
#
# **Por qué un túnel y no un puerto publicado.** En producción el panel no expone puerto ninguno:
# el compose de producción hace `ports: !reset []`, así que solo el proxy mira a internet. El panel
# permite acceso anónimo —no pide contraseña— y eso es aceptable precisamente porque no se puede
# llegar a él desde fuera. Publicarlo «solo un momento para la demostración» convertiría una
# decisión de diseño en un agujero.
#
# El reenvío apunta a la IP del contenedor dentro de la red de Docker, no a localhost del
# servidor, porque sin puerto publicado el panel no escucha en el host: solo existe dentro de esa
# red, y el servidor sí la alcanza.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"
PUERTO_LOCAL="${PUERTO_LOCAL:-18890}"

echo "==> Averiguando dónde escucha el panel dentro de la red de Docker"

IP=$(ssh -i "$LLAVE" "$MAQUINA" \
  "docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' ucokudos-observabilidad" \
  | tr -d '\r')

if [ -z "$IP" ]; then
  echo "No se pudo averiguar la dirección del contenedor. ¿Está levantado?" >&2
  exit 1
fi

echo "==> Panel en $IP:18888 dentro del servidor"
echo "==> Abriendo el túnel: http://localhost:$PUERTO_LOCAL"
echo "==> Ctrl+C para cerrarlo cuando termines"
echo

exec ssh -i "$LLAVE" -N -L "$PUERTO_LOCAL:$IP:18888" "$MAQUINA"
