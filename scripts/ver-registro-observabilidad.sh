#!/bin/sh
# Enseña qué dice el servidor sobre la exportación de telemetría (ADR-0014).
#
#   sh scripts/ver-registro-observabilidad.sh
#
# Solo lee. No cambia nada.
#
# Se mira el registro de la API y no el panel porque un exportador que no consigue conectarse
# **no deja rastro en el panel**: el panel simplemente no recibe nada, que es indistinguible de
# que la aplicación no tenga tráfico. El error, si lo hay, está en el otro extremo.
#
# **Las variables se leen con `docker inspect`, no entrando al contenedor.** La imagen de la API
# es *chiseled*: no lleva intérprete de órdenes ni utilidades, así que `exec ... printenv` falla
# con «executable file not found». Una primera versión de este guion hacía justo eso y remataba
# con `|| echo ninguna`, de modo que un fallo de la comprobación se presentaba como un resultado:
# decía que no había variables cuando lo que pasaba es que no había podido mirar.
set -eu

MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"

COMPOSE="docker compose -f docker-compose.yml -f docker-compose.produccion.yml"

ssh -i "$LLAVE" "$MAQUINA" "
  cd UcoKudos

  echo '=== Variables OTEL que recibió el contenedor de la API ==='
  docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ucokudos-api | grep -i '^OTEL' \
    || echo '(el contenedor no recibió ninguna variable OTEL)'

  echo
  echo '=== Aviso de arranque ==='
  $COMPOSE logs api 2>&1 | grep -iE 'Observabilidad:|AVISO:' | tail -3

  echo
  echo '=== Errores del exportador, si los hay ==='
  $COMPOSE logs api --tail 400 2>&1 | grep -iE 'otlp|exporter|grpc|opentelemetry|refused' | tail -20 \
    || echo '(ninguno en las últimas 400 líneas)'

  echo
  echo '=== ¿Se alcanza el panel desde la red de Docker? ==='
  # Se prueba desde el contenedor de PostgreSQL, que sí trae utilidades, porque el de la API no.
  # Ambos están en la misma red, así que lo que uno alcanza lo alcanza el otro.
  $COMPOSE exec -T postgres sh -c '
    getent hosts observabilidad || echo \"NO se resuelve el nombre observabilidad\"
    nc -z -w 3 observabilidad 18889 && echo \"Puerto OTLP 18889 ABIERTO\" || echo \"Puerto OTLP 18889 NO responde\"
  '

  echo
  echo '=== Cuándo se crearon los contenedores ==='
  docker inspect -f '{{.Name}} creado {{.Created}}' ucokudos-api ucokudos-observabilidad
"
