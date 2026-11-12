#!/bin/sh
# Abre un túnel a la base de datos del servidor, para conectarse con pgAdmin, DBeaver o psql.
#
#   sh scripts/tunel-base-de-datos.sh
#
# Deja la terminal ocupada: el túnel vive mientras no se cierre con Ctrl+C.
#
# Luego, en pgAdmin, se crea un servidor nuevo con:
#
#   Host: localhost      Puerto: 5435
#   Base: ucokudos       Usuario y contraseña: los de POSTGRES_* del .env del servidor
#
# **Por qué un túnel y no abrir el puerto.** En producción la base no publica ninguno: el compose
# hace `ports: !reset []` y solo el proxy mira a internet (ADR-0016, punto 7). Un PostgreSQL
# expuesto a internet recibe intentos de acceso en cuestión de horas. El túnel da el mismo acceso
# sin abrir nada: el tráfico viaja dentro de la conexión SSH, ya autenticada con la llave.
#
# El reenvío apunta a la IP del contenedor dentro de la red de Docker, no a localhost del
# servidor, porque sin puerto publicado la base no escucha en el host.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"
PUERTO_LOCAL="${PUERTO_LOCAL:-5435}"

echo "==> Averiguando dónde escucha PostgreSQL dentro de la red de Docker"

IP=$(ssh -i "$LLAVE" "$MAQUINA" \
  "docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' ucokudos-postgres" \
  | tr -d '\r')

if [ -z "$IP" ]; then
  echo "No se pudo averiguar la dirección del contenedor. ¿Está levantado?" >&2
  exit 1
fi

echo "==> PostgreSQL en $IP:5432 dentro del servidor"
echo
echo "    Conectar pgAdmin a:  localhost:$PUERTO_LOCAL"
echo "    Base de datos:       ucokudos"
echo "    Usuario/contraseña:  los de POSTGRES_USUARIO y POSTGRES_PASSWORD del .env del servidor"
echo
echo "==> Túnel abierto. Ctrl+C para cerrarlo cuando termines."
echo

exec ssh -i "$LLAVE" -N -L "$PUERTO_LOCAL:$IP:5432" "$MAQUINA"
