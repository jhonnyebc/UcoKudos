# ADR-0003: PostgreSQL con un ORM y migraciones versionadas como persistencia

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0003 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Persistencia |

## Contexto

El modelo de Kudos tiene relaciones densas y reglas que deben sostenerse bajo concurrencia: un rol activo por persona (RN-04), un Rector y un Coordinador Académico activos, uno por facultad y uno por programa (RF-04), y una sola calificación activa por usuario y contenido (RF-10). Las réplicas forman un árbol sin límite de profundidad (RF-09). El muro es de lectura intensiva (DRV-002) y la publicación debe confirmar el Kudo, su auditoría y su evento en una sola transacción (QA-CONF-01).

Además hay que guardar un árbol de réplicas, el estado de los eventos pendientes y una cadena de auditoría, todo con presupuesto cero (RT-07).

## Decisión

Usar **PostgreSQL** como única base de datos, con un **ORM con mapeo explícito** y **migraciones versionadas en el repositorio**.

1. **Una sola base relacional.** No se añade un motor documental ni un motor de búsqueda: las reglas de RF-04 y RF-10 son restricciones de integridad, y PostgreSQL las hace cumplir en el nivel donde no se pueden eludir.
2. **Las invariantes de unicidad viven en la base**, además de en el dominio: índices únicos parciales para «un Rector activo», «un coordinador activo por facultad» y «una calificación activa por usuario y contenido». Una validación en la aplicación no resiste dos peticiones simultáneas; un índice único sí.
3. **Mapeo explícito, sin anotaciones en las entidades.** El dominio no conoce el ORM (regla 1 de ADR-0001): la configuración del mapeo vive en la capa de infraestructura. Esto es lo que permite que las entidades se prueben sin base de datos.
4. **Migraciones versionadas**, aplicadas de forma automática al desplegar. El esquema es parte del código, no un estado manual del servidor.
5. **Convenciones de esquema:** nombres en `snake_case`, instantes guardados en UTC, enumeraciones guardadas como texto para que un volcado de la base sea legible, y claves primarias con identificadores ordenables en el tiempo para que los índices no se fragmenten al insertar.
6. **Esquemas separados** por responsabilidad: el núcleo del negocio, la auditoría y la bandeja de eventos. La tabla de auditoría admite solo inserciones, garantizado con un disparador.

## Alternativas consideradas

| Criterio | **PostgreSQL relacional (elegida)** | A. MongoDB | B. PostgreSQL con el dominio anotado | C. SQL sin ORM |
|---|---|---|---|---|
| Invariantes de unicidad (QA-SEG-03, RF-04) | Cumple — índices únicos parciales | No cumple — exige transacciones multidocumento y control en la aplicación | Cumple | Cumple |
| Transacción única para Kudo, auditoría y evento (QA-CONF-01) | Cumple | Parcial | Cumple | Cumple |
| Dominio independiente de la infraestructura (ADR-0001) | Cumple — mapeo externo | Parcial | **No cumple** — el ORM se filtra al dominio | Cumple |
| Árbol de réplicas sin límite (RF-09) | Cumple — consultas recursivas nativas | Cumple — documentos anidados, con límite de tamaño | Cumple | Cumple |
| Velocidad de desarrollo (RN-02) | Cumple | Parcial | Cumple | No cumple — mucho código repetitivo |
| Costo (RT-07) | Cumple — contenedor propio | Cumple | Cumple | Cumple |
| Lectura masiva (DRV-002) | Cumple — índices, vistas y plan de consulta inspeccionable | Parcial | Cumple | Cumple |

- **A. MongoDB** — descartada: el modelo de Kudos es relacional y, sobre todo, las reglas de unicidad de RF-04 son el tipo de invariante que un motor relacional defiende y uno documental delega a la aplicación. El contenido sin límite de longitud (RN-03) no es razón suficiente: un campo de texto de PostgreSQL no tiene límite práctico.
- **B. PostgreSQL con el dominio anotado** — descartada: es el camino corto, pero rompe la regla 1 de ADR-0001 y haría que probar una invariante exigiera levantar una base.
- **C. SQL escrito a mano sin ORM** — descartada para el conjunto: el volumen de código repetitivo no cabe en el plazo. Se usa de forma puntual donde el ORM no alcanza: el `FOR UPDATE SKIP LOCKED` del Outbox y la consulta recursiva del árbol de réplicas.

## Consecuencias

**Positivas**

- Las reglas más delicadas del enunciado quedan garantizadas por el motor, no por la buena suerte del orden de ejecución.
- Una sola dependencia de datos que desplegar, respaldar y escanear.
- El dominio se prueba sin base de datos, lo que sostiene la base de la pirámide.
- El esquema es reproducible: cualquiera puede levantar el entorno aplicando las migraciones.

**Negativas**

- Un índice único parcial es una regla escrita en dos lugares, el dominio y la base, que deben mantenerse de acuerdo. Las pruebas de integración lo verifican.
- El mapeo explícito es más código de configuración que anotar las entidades.
- Las consultas escritas a mano quedan fuera de la verificación de tipos del ORM: cada una lleva su prueba de integración contra una base real.

## Tácticas de Bass aplicadas

- **Mantener la consistencia de los datos** — una transacción protege el agregado.
- **Controlar la concurrencia** — los índices únicos rechazan la segunda escritura competidora.
- **Abstraer servicios comunes** — los repositorios ocultan el acceso a datos detrás de puertos del dominio.

## Escenarios y requisitos relacionados

- `QA-SEG-03` — el segundo Rector activo se rechaza por invariante.
- `QA-CONF-01` — una transacción cubre Kudo, auditoría y evento.
- `QA-REND-01` y `QA-REND-02` — índices y plan de consulta inspeccionable.
- `RF-04`, `RF-09`, `RF-10`, `RN-03`.

## ADRs relacionados

- **ADR-0001** — la regla que obliga al mapeo externo.
- **ADR-0005** — la transacción y el Outbox que viven en esta base.
- **ADR-0011** — el modelo del árbol de réplicas.
- **ADR-0013** — la tabla de auditoría de solo inserción.

## Notas de implementación

- Los índices del muro se diseñan a partir de las consultas reales, no al revés: la lista por fecha descendente y la lista por destinatario son las dos que mandan.
- Toda consulta escrita a mano se verifica con una prueba de integración contra PostgreSQL real en un contenedor desechable, no contra una base en memoria: una base en memoria no reproduce el comportamiento de los índices parciales ni del bloqueo.
- Las migraciones nunca se editan después de aplicarse: se añade una nueva.

## Referencias

- PostgreSQL Global Development Group. *PostgreSQL Documentation — Indexes, Constraints, Recursive Queries.*
- Fowler, M. *Patterns of Enterprise Application Architecture* — Repository, Data Mapper.
