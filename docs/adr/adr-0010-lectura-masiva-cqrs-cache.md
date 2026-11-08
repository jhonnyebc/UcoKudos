# ADR-0010: responder a la alta demanda de lectura con CQRS ligero, caché por evento y paginación por cursor

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0010 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Persistencia y Rendimiento |

## Contexto

DRV-002 declara que «se espera un alto volumen de lecturas, por lo que las decisiones arquitectónicas deberán justificar cómo responden a este escenario». Es el driver más explícito del enunciado después de la movilidad, y exige una respuesta concreta, no una declaración de intenciones.

El perfil de uso de una plataforma de reconocimientos es muy asimétrico. Un Kudo se escribe una vez y se lee muchas: lo ve el destinatario, su programa, su facultad y cualquiera que entre al muro. Las réplicas y las calificaciones multiplican las lecturas sobre el mismo contenido. Un cálculo conservador para una universidad mediana da un orden de magnitud de cien lecturas por cada escritura.

Además, el 90 % de esas lecturas llega desde un celular en red móvil (DRV-001), donde cada petición adicional cuesta latencia, y la pantalla más visitada es el muro con los contadores de cada Kudo.

El riesgo concreto a evitar es el problema de las N+1 consultas: una lista de 20 Kudos que, por cada uno, consulte su autor, su categoría, su número de réplicas y su promedio de calificaciones, son 81 consultas para pintar una pantalla.

## Decisión

Cuatro mecanismos complementarios, cada uno atacando una causa distinta de lentitud.

### 1. CQRS ligero: separar el modelo de lectura del de escritura

Las escrituras pasan por el dominio, con sus entidades, invariantes y transacciones. Las lecturas **no**: van por un puerto de consultas aparte que devuelve directamente los datos que la pantalla necesita, en una sola consulta, sin materializar entidades de dominio.

Es «ligero» porque **no hay dos bases de datos ni sincronización entre ellas**: es la misma base, con dos caminos de acceso. Se evita así la consistencia eventual entre modelos, que no está justificada por ningún driver, y se conserva el beneficio principal: la lectura no arrastra el costo del modelo de escritura.

### 2. Contadores materializados en lugar de agregaciones en cada lectura

El número de réplicas y el promedio de calificaciones de un Kudo **no se calculan al leer**: se guardan como columnas en el propio Kudo y se actualizan cuando llega una réplica o una calificación, dentro de la misma transacción.

Esto convierte una agregación sobre todo el árbol de réplicas en la lectura de una columna. Es la decisión que más impacto tiene en el muro.

### 3. Caché de lectura con invalidación por evento

Las consultas más repetidas y menos volátiles se guardan en caché con el patrón **cache-aside**: el catálogo de categorías, la estructura académica de facultades y programas, y la primera página del muro.

La invalidación **no es por tiempo de expiración, sino por evento**: publicar un Kudo invalida la primera página del muro; administrar una categoría invalida el catálogo. Una expiración por tiempo obliga a elegir entre datos viejos y poca eficacia; la invalidación por evento no tiene ese compromiso, y aquí es posible porque ADR-0005 ya produce esos eventos.

La caché es **en memoria del proceso** y vive detrás de un puerto. Hoy hay una sola instancia, así que una caché distribuida añadiría un salto de red sin ningún beneficio. Cuando haya más de una instancia, se sustituye el adaptador por uno distribuido: el puerto no cambia (QA-ELAS-01).

### 4. Paginación por cursor, no por número de página

El muro se pagina con un cursor (fecha y identificador del último elemento visto), no con desplazamiento y límite. Dos razones:

- El desplazamiento obliga al motor a recorrer y descartar todas las filas anteriores: la página 50 es mucho más lenta que la página 1.
- Con contenido que se inserta constantemente, el desplazamiento muestra elementos repetidos o se salta otros al pasar de página.

El cursor es, además, el modelo correcto para el desplazamiento infinito de una interfaz móvil (DRV-001).

### 5. Caché en el cliente con validadores HTTP

Las respuestas de lectura llevan un validador de contenido. Si el cliente vuelve a pedir lo mismo y no cambió, el servidor responde «no modificado» sin cuerpo. En red móvil esto ahorra transferencia real.

## Alternativas consideradas

| Criterio | **CQRS ligero + caché + cursor (elegida)** | A. Leer por el modelo de dominio | B. CQRS con base de lectura separada | C. Solo caché por tiempo | D. Réplica de lectura de PostgreSQL |
|---|---|---|---|---|---|
| Responde a DRV-002 | Cumple | **No cumple** — N+1 consultas por pantalla | Cumple | Parcial | Cumple |
| Datos siempre actuales | Cumple — invalidación por evento | Cumple | **No cumple** — consistencia eventual | No cumple — ventana de datos viejos | Parcial — retraso de réplica |
| Complejidad operativa (RT-07, RN-02) | Cumple — ninguna pieza nueva | Cumple | **No cumple** — proyecciones y sincronización | Cumple | No cumple — otra instancia de base |
| Páginas profundas estables | Cumple — cursor | No cumple | Cumple | No aplica | No cumple por sí sola |
| Camino a escalar (QA-ELAS-01) | Cumple — cambio de adaptador | No cumple | Cumple | Parcial | Cumple |
| Costo (RT-07) | Cumple | Cumple | No cumple | Cumple | **No cumple** — duplica la base |

- **A. Leer por el modelo de dominio** — descartada: es lo que haría un ORM por omisión y produce exactamente el problema N+1 que DRV-002 obliga a evitar. Cargar un agregado completo para mostrar una línea de una lista es trabajo desperdiciado.
- **B. CQRS completo con base de lectura separada** — descartada: es la respuesta de manual a una carga de lectura intensiva, pero introduce proyecciones, sincronización y consistencia eventual en los datos que el usuario ve. El volumen de una universidad no lo justifica, y el plazo menos.
- **C. Solo caché con expiración por tiempo** — descartada como mecanismo principal: es el más simple, pero obliga a aceptar que un Kudo recién publicado no aparezca durante el tiempo de expiración. Con los eventos de ADR-0005 disponibles, invalidar por evento no cuesta más y no tiene ese defecto.
- **D. Réplica de lectura** — descartada hoy: duplicaría el costo de la base, que con presupuesto cero significa duplicar el consumo de la única máquina. Queda documentada como la evolución cuando el cuello de botella sea demostrablemente la base y no la aplicación.

## Consecuencias

**Positivas**

- Cada pantalla se pinta con un número de consultas acotado y conocido, no proporcional a los elementos que muestra.
- Los contadores materializados eliminan la agregación más costosa del sistema.
- Los datos que el usuario ve están siempre actualizados, porque la invalidación es por evento.
- El camino para escalar está identificado y no exige rediseñar: cambiar el adaptador de caché y, después, añadir una réplica de lectura.
- Menos transferencia en red móvil, que es donde DRV-001 lo hace notar.

**Negativas**

- **Los contadores materializados son datos derivados que pueden quedar desfasados** si una escritura los omite. Es el riesgo real de esta decisión. Se mitiga de dos formas: se actualizan dentro de la misma transacción que la réplica o la calificación, y existe una consulta de verificación que compara el contador con el valor real, usada en las pruebas de integración.
- Hay dos caminos de acceso a los datos, así que un cambio en el modelo puede exigir tocar el de escritura y el de consultas.
- Las consultas de lectura escritas a mano no pasan por la verificación de tipos del ORM: cada una lleva su prueba de integración (ADR-0003).
- La caché en memoria no sirve con varias instancias, y hay que recordarlo antes de escalar.

## Tácticas de Bass aplicadas

- **Mantener múltiples copias de los datos** — contadores materializados y caché.
- **Aumentar los recursos disponibles** — reducir el trabajo por petición en lugar de pedir más máquina.
- **Reducir la demanda computacional** — una consulta por pantalla, agregaciones precalculadas.
- **Introducir concurrencia** — la lectura no compite con la ruta de escritura.
- **Limitar el tamaño de las colas** — paginación por cursor con tamaño acotado.

## Escenarios y requisitos relacionados

- `QA-REND-01` — el muro con 10 000 Kudos desde un celular: P95 < 500 ms.
- `QA-REND-02` — 200 usuarios concurrentes sin degradación.
- `QA-ELAS-01` — el camino a varias instancias.
- `RF-13`, `RF-09`, `RF-10`, `DRV-002`, `DRV-001`.

## Notas de implementación

- La afirmación de que esto responde a DRV-002 hay que **medirla**, no suponerla. Se incluye una prueba de carga con datos sembrados (del orden de 10 000 Kudos y sus réplicas) que reporte los percentiles de QA-REND-01. Esa medición es la evidencia que pide el enunciado y el argumento de la sustentación.
- Los índices se diseñan a partir de las consultas reales, y se verifica el plan de ejecución de las dos consultas del muro para confirmar que usan el índice y no recorren la tabla.
- La caché guarda los datos ya listos para la respuesta, no entidades: así se evita volver a transformarlos en cada lectura.
- Cada entrada de caché declara qué eventos la invalidan, en un solo lugar. Una invalidación dispersa por el código es una fuente garantizada de datos viejos.

## ADRs relacionados

- **ADR-0003** — los índices y las consultas escritas a mano.
- **ADR-0005** — los eventos que disparan la invalidación.
- **ADR-0011** — el árbol de réplicas, que se lee con el mismo criterio.
- **ADR-0015** — el cliente que consume estas lecturas.

## Referencias

- Fowler, M. *CQRS* y *Patterns of Enterprise Application Architecture.*
- Microsoft. *Cache-Aside pattern* y *Materialized View pattern.* Cloud Design Patterns.
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice* — tácticas de rendimiento.
