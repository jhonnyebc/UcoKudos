#!/bin/sh
# Crea la persona de una cuenta que ya existe en el proveedor de identidad, como Docente y ya activa.
#
#   ./scripts/crear-cuenta-docente.sh <correo> "<nombre completo>" [celular]
#
# Se ejecuta EN EL SERVIDOR, desde ~/UcoKudos. Es idempotente: repetirlo no duplica nada y, si la
# persona ya existe, solo le corrige el nombre.
#
# **Por qué hace falta este paso además de crear el usuario en Auth0.** En esta plataforma
# autenticarse no es tener cuenta (ADR-0004): el proveedor de identidad dice *quién es* quien llama,
# y la persona —su estado, su rol, su ámbito— vive en la base y se resuelve en cada petición. Un
# usuario de Auth0 sin persona recibe «No existe una cuenta para ...», y es lo correcto.
#
# **Por qué los datos de la persona van como parámetros y no escritos en el guion.** Este archivo
# está en un repositorio público. El correo y el nombre de alguien que no es el autor no tienen por
# qué estar ahí.
#
# **Por qué Docente.** Es el rol que ve lo mismo que cualquier usuario de la plataforma: publica,
# lee, adjunta, recibe avisos. Las pantallas de Categorías y Auditoría le responden con el motivo
# exacto —«el rol Docente no administra categorías»—, que es la autorización funcionando.
#
# El celular es opcional y por omisión es un número ficticio: el canal de WhatsApp intentaría
# entregarle los avisos y Meta lo rechazaría, lo cual es inofensivo. Si la persona quiere recibirlos
# de verdad, su número tendría que estar registrado como destinatario de prueba en el panel de Meta.
set -eu

CORREO="${1:-}"
NOMBRE="${2:-}"
CELULAR="${3:-+573000000099}"

if [ -z "$CORREO" ] || [ -z "$NOMBRE" ]; then
  echo "Uso: ./scripts/crear-cuenta-docente.sh <correo> \"<nombre completo>\" [celular]" >&2
  exit 1
fi

# El correo se guarda en minúsculas y sin espacios. La API normaliza al buscar, pero lo que se
# escribe en la base tiene que coincidir con lo que normaliza.
CORREO=$(printf '%s' "$CORREO" | tr '[:upper:]' '[:lower:]' | tr -d ' ')

case "$CORREO" in
  *@*.*) ;;
  *)
    echo "«$CORREO» no parece un correo." >&2
    exit 1
    ;;
esac

USUARIO=$(sed -n 's/^POSTGRES_USUARIO=//p' .env)
BASE=$(sed -n 's/^POSTGRES_BD=//p' .env)
USUARIO="${USUARIO:-ucokudos}"
BASE="${BASE:-ucokudos}"

# Sobrescribible para poder probarlo contra una base local: `COMPOSE="docker compose" ./scripts/...`.
COMPOSE="${COMPOSE:-docker compose -f docker-compose.yml -f docker-compose.produccion.yml}"

# El programa de la persona es el de la demostración. Si no existe, se avisa en vez de crear una
# asignación que la base rechazaría con un error de clave externa.
PROGRAMA=$($COMPOSE exec -T postgres psql -U "$USUARIO" -d "$BASE" -t -A -q \
  -c "SELECT count(*) FROM nucleo.programa WHERE id = '0193b000-0000-7000-8000-000000000002';")

if [ "$PROGRAMA" != "1" ]; then
  echo "No existe el programa de la demostración. Ejecutar antes el sembrado de la demostración." >&2
  exit 1
fi

echo "==> Creando la persona $CORREO como Docente"

# Las variables de psql (-v y :'nombre') escapan el contenido como literal: un nombre con comilla
# simple, como «D'Alessandro», no rompe la sentencia ni permite inyectar SQL.
$COMPOSE exec -T postgres psql -U "$USUARIO" -d "$BASE" -q -v ON_ERROR_STOP=1 \
  -v correo="$CORREO" -v nombre="$NOMBRE" -v celular="$CELULAR" <<'SQL'
BEGIN;

INSERT INTO nucleo.persona (
    id, correo_institucional, celular_e164, nombre_completo,
    correo_verificado, celular_verificado, solicitud_aprobada, estado_cuenta, creada_en)
VALUES (gen_random_uuid(), :'correo', :'celular', :'nombre', true, true, true, 'Activa', now())
ON CONFLICT (correo_institucional) DO UPDATE
    SET nombre_completo = EXCLUDED.nombre_completo;

-- Rol 6 = Docente, con ámbito de programa. Solo si no tiene ya un rol activo: hay un índice único
-- parcial que impone un rol activo por persona (RF-04), y esto no debe depender de él para no
-- fallar al repetirse.
INSERT INTO nucleo.asignacion_rol (id, persona_id, rol_id, facultad_id, programa_id, activa, desde)
SELECT gen_random_uuid(), p.id, 6,
       '0193b000-0000-7000-8000-000000000001', '0193b000-0000-7000-8000-000000000002', true, now()
FROM nucleo.persona p
WHERE p.correo_institucional = :'correo'
  AND NOT EXISTS (
      SELECT 1 FROM nucleo.asignacion_rol a WHERE a.persona_id = p.id AND a.activa);

COMMIT;
SQL

echo
echo "==> Comprobación: la cuenta tal como quedó"
$COMPOSE exec -T postgres psql -U "$USUARIO" -d "$BASE" -P pager=off \
  -v correo="$CORREO" <<'SQL'
SELECT p.nombre_completo, p.correo_institucional, p.estado_cuenta,
       a.rol_id, a.activa AS rol_activo
FROM nucleo.persona p
LEFT JOIN nucleo.asignacion_rol a ON a.persona_id = p.id AND a.activa
WHERE p.correo_institucional = :'correo';
SQL
