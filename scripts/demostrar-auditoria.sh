#!/bin/sh
# Demuestra las DOS defensas de la auditoría (ADR-0013), contra el sistema desplegado.
#
#   sh scripts/demostrar-auditoria.sh intento-directo   # el disparador rechaza el cambio
#   sh scripts/demostrar-auditoria.sh romper            # lo salta y altera una fila
#   sh scripts/demostrar-auditoria.sh restaurar         # deja todo como estaba
#
# **Por qué hacen falta dos pasos y no uno.** La tabla tiene un disparador que rechaza toda
# actualización y todo borrado, así que ni la propia aplicación puede alterar el registro. Eso
# **impide** la manipulación. La cadena de hashes **la detecta**, que es distinto y sirve para el
# caso en que alguien tenga permisos para desactivar el disparador.
#
# Enseñar solo el disparador deja sin demostrar la cadena; enseñar solo la cadena hace pensar que
# cualquiera puede editar la auditoría. Las dos juntas son la historia completa.
#
# El paso `romper` desactiva el disparador a propósito, imitando a quien llega con permisos de
# administrador de la base. Guarda la fila original en una tabla de respaldo antes de tocarla, y
# `restaurar` la devuelve a su sitio: la demostración no deja residuos.
set -eu

ACCION="${1:-}"
MAQUINA="${MAQUINA:-azureuser@20.83.117.236}"
LLAVE="${LLAVE:-$HOME/.ssh/ucokudos_key.pem}"

if [ -z "$ACCION" ]; then
  echo "Falta la acción: intento-directo, romper o restaurar." >&2
  exit 1
fi

ejecutar_sql() {
  ssh -i "$LLAVE" "$MAQUINA" "
    cd UcoKudos
    USUARIO=\$(sed -n 's/^POSTGRES_USUARIO=//p' .env)
    BASE=\$(sed -n 's/^POSTGRES_BD=//p' .env)
    docker compose -f docker-compose.yml -f docker-compose.produccion.yml exec -T postgres \
      psql -U \"\${USUARIO:-ucokudos}\" -d \"\${BASE:-ucokudos}\" -P pager=off -v ON_ERROR_STOP=0 -c \"$1\"
  "
}

# Se elige una fila intermedia y no la última: si se alterase la última, alguien podría pensar que
# la cadena solo mira el final. Alterar una del medio obliga a recorrerla de verdad.
FILA="(SELECT id FROM audit.evento_auditoria ORDER BY id OFFSET 3 LIMIT 1)"

case "$ACCION" in
  intento-directo)
    echo "==> Intentando editar la auditoría como lo haría cualquiera con acceso a la base"
    echo
    ejecutar_sql "UPDATE audit.evento_auditoria SET accion = 'ALTERADO' WHERE id = $FILA;"
    echo
    echo "Se espera un error: la tabla es de solo inserción (ADR-0013)."
    ;;

  romper)
    echo "==> Guardando la fila original y desactivando el disparador"
    ejecutar_sql "
      CREATE TABLE IF NOT EXISTS audit.respaldo_demostracion AS
        SELECT * FROM audit.evento_auditoria WHERE false;
      DELETE FROM audit.respaldo_demostracion;
      INSERT INTO audit.respaldo_demostracion
        SELECT * FROM audit.evento_auditoria WHERE id = $FILA;
      ALTER TABLE audit.evento_auditoria DISABLE TRIGGER tr_auditoria_solo_insercion;
      UPDATE audit.evento_auditoria SET accion = 'ALTERADO' WHERE id = $FILA;
      ALTER TABLE audit.evento_auditoria ENABLE TRIGGER tr_auditoria_solo_insercion;
      SELECT id, accion FROM audit.evento_auditoria WHERE id IN (SELECT id FROM audit.respaldo_demostracion);
    "
    echo
    echo "Fila alterada. Ahora pulsar «Verificar la cadena» en la pantalla de Auditoría."
    ;;

  restaurar)
    echo "==> Devolviendo la fila a su valor original"
    ejecutar_sql "
      ALTER TABLE audit.evento_auditoria DISABLE TRIGGER tr_auditoria_solo_insercion;
      UPDATE audit.evento_auditoria e
        SET accion = r.accion
        FROM audit.respaldo_demostracion r
        WHERE e.id = r.id;
      ALTER TABLE audit.evento_auditoria ENABLE TRIGGER tr_auditoria_solo_insercion;
      DROP TABLE IF EXISTS audit.respaldo_demostracion;
    "
    echo
    echo "Restaurado. Verificar otra vez: la cadena debe volver a dar íntegra."
    ;;

  *)
    echo "Acción desconocida: $ACCION" >&2
    exit 1
    ;;
esac
