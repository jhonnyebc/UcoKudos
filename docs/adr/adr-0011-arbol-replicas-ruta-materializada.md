# ADR-0011: modelar el árbol de réplicas con referencia al padre y ruta materializada

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0011 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Persistencia |

## Contexto

RF-09 permite réplicas sobre Kudos y réplicas sobre otras réplicas, **sin límite de profundidad, sin límite de cantidad y sin límite de longitud**. Es una estructura de árbol de profundidad arbitraria.

Esto choca de frente con DRV-002: la operación más frecuente no es insertar una réplica, sino **leer el hilo completo de un Kudo** para mostrarlo. Si leer un hilo de profundidad 10 exige 10 consultas encadenadas, el muro se vuelve inviable.

El enunciado además permite calificar réplicas (RF-10) y vetarlas (RF-12). Vetar una réplica debe ocultar también sus descendientes: no tiene sentido ocultar un comentario y dejar visibles las respuestas que lo citan.

## Decisión

Guardar las réplicas en **una sola tabla** con dos columnas estructurales complementarias:

1. **Referencia al padre** (`replica_padre_id`, nula en una réplica de primer nivel) y referencia al Kudo raíz. Es la verdad de la estructura y sostiene la integridad referencial.
2. **Ruta materializada** (`ruta`): una cadena con los identificadores de todos los ancestros, por ejemplo `/a1/7f/c3/`. Es un dato derivado, calculado al insertar a partir de la ruta del padre, con un índice que permite buscar por prefijo.

Con esas dos columnas:

- **Leer un hilo completo es una sola consulta:** todas las réplicas cuya ruta empiece por la del Kudo, ordenadas por ruta. Ese orden es exactamente el orden de presentación anidado del hilo, así que el cliente no tiene que reconstruir el árbol: lo pinta en el orden que recibe. La profundidad se deduce de la propia ruta.
- **Vetar en cascada es una sola operación:** marcar todas las réplicas cuya ruta empiece por la de la vetada (RF-12).
- **Contar las réplicas de un Kudo** no recorre nada: es el contador materializado de ADR-0010.

La profundidad no tiene límite de negocio. Sí hay un **límite técnico defensivo** en la longitud de la ruta, lo bastante alto para no interferir con ningún uso legítimo y suficiente para impedir que una secuencia automatizada de respuestas a respuestas degrade el índice. Al alcanzarlo, la réplica se cuelga del último ancestro válido en lugar de rechazarse.

## Alternativas consideradas

| Criterio | **Padre + ruta materializada (elegida)** | A. Solo padre, con consulta recursiva | B. Tabla de cierre transitivo | C. Documento anidado en una columna JSON | D. Tipo jerárquico del motor |
|---|---|---|---|---|---|
| Leer el hilo completo (DRV-002) | Cumple — una consulta por prefijo de índice | Parcial — una consulta recursiva, más costosa | Cumple — una unión | Cumple — una fila | Cumple |
| Profundidad ilimitada (RF-09) | Cumple | Cumple | Cumple | **No cumple** — límite de tamaño de la fila | Cumple |
| Orden de presentación anidado sin trabajo en el cliente | Cumple — el orden por ruta es el orden del hilo | No cumple — hay que reconstruir el árbol | No cumple | Cumple | Parcial |
| Veto en cascada (RF-12) | Cumple — una operación por prefijo | Parcial — recursiva | Cumple | Cumple | Cumple |
| Calificar una réplica (RF-10) | Cumple — cada réplica es una fila con identidad | Cumple | Cumple | **No cumple** — no tiene identidad propia | Cumple |
| Espacio y complejidad de escritura | Cumple — una columna de texto | Cumple — ninguna | **No cumple** — una fila por par ancestro-descendiente | Cumple | Parcial — extensión del motor |
| Portabilidad | Cumple — texto estándar | Cumple | Cumple | Cumple | **No cumple** — ata al motor |

- **A. Solo referencia al padre con consulta recursiva** — descartada como mecanismo principal. Es la opción más limpia conceptualmente y la primera que uno escribe, pero una consulta recursiva recorre el árbol nivel por nivel y devuelve los resultados en un orden que el cliente debe reorganizar. Con DRV-002 de por medio, la ruta materializada entrega lo mismo con un recorrido de índice por prefijo. Se conserva la referencia al padre **junto** con la ruta, porque es la que da integridad referencial: la ruta sola no impide apuntar a un padre inexistente.
- **B. Tabla de cierre transitivo** — descartada: es la estructura más rápida para consultar ancestros y descendientes en cualquier dirección, pero guarda una fila por cada par de ancestro y descendiente. Con réplicas ilimitadas ese crecimiento es cuadrático en la profundidad, y ninguna consulta del enunciado necesita la potencia que ofrece.
- **C. Documento anidado en una columna JSON** — descartada: el límite de tamaño de una fila contradice «sin límite de cantidad», y sobre todo las réplicas dejarían de tener identidad propia, lo que impide calificarlas (RF-10) y vetarlas individualmente (RF-12).
- **D. Tipo jerárquico específico del motor** — descartada: resolvería el problema de forma elegante, pero exige habilitar una extensión en el servidor y ata el modelo a ese motor. Una columna de texto con un índice de prefijo logra lo mismo sin esa dependencia, y es más fácil de explicar.

## Consecuencias

**Positivas**

- Leer un hilo, por profundo que sea, es una consulta y un recorrido de índice.
- El cliente recibe las réplicas en el orden en que debe pintarlas, con su profundidad incluida: menos trabajo en el celular, que es donde importa (DRV-001).
- Vetar en cascada es inmediato y correcto por construcción.
- Cada réplica conserva identidad propia, así que se puede calificar, reportar y vetar individualmente.

**Negativas**

- **La ruta es un dato derivado y puede quedar inconsistente** con la referencia al padre si una inserción la calcula mal. Es el riesgo principal. Se mitiga calculándola en un solo lugar, dentro del propio dominio al crear la réplica, y con una consulta de verificación que compara ruta y padre, ejecutada en las pruebas de integración.
- Mover una réplica de padre obligaría a reescribir la ruta de todo el subárbol. El enunciado no contempla mover réplicas, así que la operación no existe; si llegara a existir, hay que recordar este costo.
- El límite técnico de longitud de ruta es una decisión que hay que declarar y justificar, porque el enunciado dice «sin límite de profundidad». Es un límite defensivo, no de negocio, y no degrada la funcionalidad: la réplica se acepta igual.

## Tácticas de Bass aplicadas

- **Mantener múltiples copias de los datos** — la ruta es una copia derivada de la estructura, mantenida para leer rápido.
- **Reducir la demanda computacional** — un recorrido de índice en lugar de una recursión.
- **Mantener la consistencia de los datos** — la ruta se calcula en la misma transacción que la réplica.

## Escenarios y requisitos relacionados

- `QA-REND-01` — el hilo de un Kudo se lee en una consulta.
- `RF-09` — profundidad y cantidad ilimitadas.
- `RF-10` — cada réplica es calificable porque tiene identidad.
- `RF-12` — veto en cascada.
- `DRV-002`, `DRV-001`, `RN-03`.

## ADRs relacionados

- **ADR-0003** — la base, el índice de prefijo y la verificación por integridad.
- **ADR-0010** — el contador materializado de réplicas y el criterio de lectura.

## Notas de implementación

- La ruta se construye con identificadores de longitud fija y un separador, para que el prefijo de un ancestro nunca coincida parcialmente con el de otro. Usar identificadores de longitud variable sin separador es el error que hace que `/1/` coincida con `/12/`.
- El índice sobre la ruta debe ser uno que soporte búsqueda por prefijo con el ordenamiento de texto adecuado; se verifica con el plan de ejecución que la consulta del hilo lo usa.
- La consulta de verificación que compara la ruta reconstruida desde el padre con la ruta guardada corre en las pruebas de integración sobre un árbol sembrado de varios niveles. Sin esa prueba, la inconsistencia aparecería en producción.
- El contenido de la réplica no tiene límite de longitud (RN-03), así que la columna de texto es ilimitada; lo que se pagina es la cantidad de réplicas, no su contenido.

## Referencias

- Celko, J. *Trees and Hierarchies in SQL for Smarties.*
- PostgreSQL Global Development Group. *Documentation — Pattern Matching, Indexes on Expressions, WITH Queries (Recursive).*
- Fowler, M. *Patterns of Enterprise Application Architecture.*
