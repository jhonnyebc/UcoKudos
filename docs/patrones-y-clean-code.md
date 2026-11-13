# Patrones de diseño, SOLID y Clean Code

El enunciado es explícito: **no se evalúa la cantidad de patrones**, sino su pertinencia, su correcta aplicación y el beneficio obtenido. Por eso este documento no es un catálogo. Cada patrón aparece con el problema concreto que resuelve, y hay un apartado final con los que se **descartaron**, que suele ser más revelador.

## 1. Patrones de diseño aplicados

### Repository

| | |
|---|---|
| **Problema** | El dominio necesita leer y guardar agregados, pero no puede conocer el ORM (regla 1 de ADR-0001) |
| **Aplicación** | Las interfaces de repositorio se declaran **en el dominio**; las implementaciones viven en infraestructura |
| **Beneficio** | Las invariantes se prueban sin levantar una base de datos, que es lo que sostiene la base de la pirámide |
| **Riesgo de mala aplicación** | Un repositorio que expone `IQueryable` filtra el ORM al dominio y anula el beneficio. Aquí los métodos devuelven agregados o listas, nunca consultas componibles |

### Unit of Work

| | |
|---|---|
| **Problema** | Publicar un Kudo debe escribir el Kudo, su evento de auditoría y su evento pendiente **todo o nada** (QA-CONF-01) |
| **Aplicación** | `IUnidadTrabajo` confirma los tres en una transacción. Los repositorios **no** guardan por su cuenta |
| **Beneficio** | Es lo que cierra la ventana «commit exitoso, envío perdido». Sin este patrón, cada repositorio confirmaría por separado y habría tres momentos para fallar |
| **Nota** | Que los repositorios no confirmen es parte del patrón, no un detalle: si uno lo hiciera, la atomicidad se rompería sin que nada avisara |

### Transactional Outbox

| | |
|---|---|
| **Problema** | Los efectos externos no pueden ir dentro de la transacción (bloquearían al usuario y un 503 rompería la publicación) ni después (se perderían si el proceso muere) |
| **Aplicación** | El evento se escribe en una tabla dentro de la transacción; un proceso lo toma después con `FOR UPDATE SKIP LOCKED` |
| **Beneficio** | Responde a QA-CONF-01, QA-DISP-01 y QA-ELAS-01 con un solo mecanismo, y es el cimiento de los cuatro rasgos del Manifiesto Reactivo |
| **Por qué es el patrón central** | Es el que convierte «publicar un Kudo con cinco efectos» de un problema difícil en uno resuelto (ADR-0005) |

### Adapter

| | |
|---|---|
| **Problema** | Cuatro proveedores externos con APIs distintas, y ninguno puede filtrarse al dominio |
| **Aplicación** | Un adaptador por proveedor detrás de un puerto. El de correo tiene **tres** implementaciones: simulada en memoria, captura local y proveedor real |
| **Beneficio** | QA-MANT-01 literal. Y algo práctico: el adaptador simulado permite probar el flujo completo de RF-07 en el pipeline, sin red y sin gastar cuota |

### Ports and Adapters (arquitectura, no patrón de GoF)

| | |
|---|---|
| **Problema** | Mantener el dominio independiente de todo lo externo |
| **Aplicación** | Puertos de **entrada** (lo que la API puede llamar) y de **salida** (lo que el módulo necesita), con sus adaptadores a cada lado |
| **Beneficio** | Es lo que hace verificables las cinco reglas de ADR-0001 |
| **Nota** | Los puertos de salida viven **en el módulo que los necesita**, no en una carpeta común: así se ve de un vistazo de qué depende cada módulo |

### Strategy

| | |
|---|---|
| **Problema** | Cada tipo de aviso usa canales distintos: un Kudo publicado va por correo, WhatsApp y tiempo real; una solicitud aprobada solo por correo |
| **Aplicación** | La política de canal es una tabla explícita por tipo de notificación, y cada canal es una estrategia intercambiable |
| **Beneficio** | Añadir un canal es una implementación más y una fila en la política, sin tocar ningún módulo emisor (ADR-0006) |
| **Riesgo que evita** | La alternativa real era una cadena de condicionales repartida por los casos de uso |

### Mediator

| | |
|---|---|
| **Problema** | Si cada caso de uso llamara a cada canal, las reglas de ventana de WhatsApp, los reintentos y la deduplicación se repetirían en todos |
| **Aplicación** | El gateway de notificaciones es el único punto de salida. Los emisores expresan **intención**, no canal |
| **Beneficio** | Las reglas de cada proveedor se escriben una vez. Los módulos de negocio quedan limpios de detalles de mensajería |

### Cache-Aside

| | |
|---|---|
| **Problema** | El muro es la pantalla más visitada y la carga es predominantemente de lectura (DRV-002) |
| **Aplicación** | Se consulta la caché; si no está, se va a la base y se guarda. **La invalidación es por evento, no por expiración** |
| **Beneficio** | La invalidación por tiempo obliga a elegir entre datos viejos y poca eficacia. Por evento no tiene ese compromiso, y aquí es posible porque la bandeja ya produce esos eventos |
| **Nota** | La caché está detrás de un puerto, así que pasar a una distribuida es cambiar un adaptador (ADR-0010) |

### Materialized View (contadores materializados)

| | |
|---|---|
| **Problema** | Mostrar el total de réplicas y el promedio de calificaciones de cada Kudo del muro exigiría agregar sobre todo el árbol, por cada fila |
| **Aplicación** | Se guardan como columnas del Kudo y se actualizan en la misma transacción que la réplica o la calificación |
| **Beneficio** | Es la decisión con más impacto en el muro: convierte una agregación en la lectura de una columna |
| **Contrapartida declarada** | Son datos derivados que pueden quedar desfasados. Se mitiga con la misma transacción y con una consulta de verificación en las pruebas |

### Materialized Path (árbol de réplicas)

| | |
|---|---|
| **Problema** | Réplicas sin límite de profundidad (RF-09), con la lectura del hilo completo como operación más frecuente |
| **Aplicación** | `replica_padre_id` da integridad referencial; una columna `ruta` con los ancestros, indexada por prefijo, da la lectura |
| **Beneficio** | Leer un hilo de cualquier profundidad es **una** consulta, el orden por ruta ya es el orden anidado de presentación, y vetar en cascada es una operación |
| **Contrapartida declarada** | La ruta es derivada y puede quedar inconsistente con el padre. Se calcula en un solo lugar y se verifica con una prueba (ADR-0011) |

### Valet Key (URL prefirmada) — implementado y abandonado

| | |
|---|---|
| **Problema** | Servir adjuntos privados sin que los megabytes atraviesen la API ni exponer las credenciales del almacenamiento |
| **Aplicación original** | La API comprobaba permisos y **entonces** emitía una URL firmada de vida corta. El cliente subía y bajaba directo |
| **Por qué se abandonó** | Falló con un 403 en el primer uso real (2026-11-13): el servidor de objetos valida las firmas con un secreto fijo y público, y las pruebas compartían esa misma suposición, así que no podían verlo. Firmar con un secreto de verdad hacía rechazar todo; firmar con el público habría dejado a cualquiera fabricar firmas válidas |
| **Lo que hay hoy** | Los archivos pasan por la API, con la sesión de quien actúa. Cuesta hasta 5 MiB por archivo que cruzan la aplicación, y a cambio la autorización y la auditoría ocurren en cada subida y en cada descarga, y el almacenamiento no tiene ruta pública (ADR-0012, revisión del 2026-11-13) |

### Specification (ligero), en las reglas de moderación

| | |
|---|---|
| **Problema** | Las reglas de quién puede vetar y sobre qué se consultan desde varios casos de uso |
| **Aplicación** | Reglas con nombre propio en servicios de dominio, consultables y comprobables por separado |
| **Beneficio** | Las reglas de RF-06 y RF-12 se prueban una por una, en lugar de a través del caso de uso que las usa |

### Patrón de la bandeja de eventos como planificador

| | |
|---|---|
| **Problema** | Los avisos programados, como un recordatorio, necesitarían un planificador aparte |
| **Aplicación** | La bandeja admite un instante de próximo intento en el futuro |
| **Beneficio** | Un componente menos que operar, vigilar y explicar |

## 2. Patrones descartados y por qué

Esto suele decir más del criterio que la lista de los aplicados.

| Patrón | Por qué no |
|---|---|
| **CQRS completo con base de lectura separada** | Es la respuesta de manual a una carga de lectura intensiva, y aquí introduciría proyecciones, sincronización y consistencia eventual **en los datos que el usuario ve**. El volumen de una universidad no lo justifica. Se aplicó la versión ligera: dos caminos de acceso, una sola base (ADR-0010) |
| **Event Sourcing** | Tentador porque ya hay eventos y una auditoría encadenada. Pero el estado del negocio se consulta muchísimo más de lo que cambia, y reconstruirlo desde eventos castigaría justo el driver dominante. La auditoría cubre la necesidad real, que es rendir cuentas, no reconstruir estado |
| **Saga** | No hay transacciones distribuidas: una sola base y una sola transacción local. Una saga sin distribución es complejidad sin problema |
| **Specification completo con composición de expresiones** | Las consultas del muro son pocas y conocidas. Un árbol de especificaciones componibles sería infraestructura para una flexibilidad que nadie pidió |
| **Decorator para reintentos** | Los reintentos viven en la bandeja, que es donde está el estado del intento. Un decorador reintentaría en memoria y perdería el intento si el proceso muere |
| **Singleton** | No se usa como patrón. El contenedor de dependencias gestiona los ciclos de vida; un singleton escrito a mano es estado global disfrazado |
| **Factory abstracta** | No hay familias de productos que variar en conjunto. Los adaptadores se eligen por configuración en la raíz de composición, que es más simple y más explícito |

## 3. Principios SOLID

No como definiciones, sino señalando dónde se aplica cada uno y qué pasaría si no.

### S — Responsabilidad única

Cada caso de uso es **una** intención del negocio. `PublicarKudoCasoUso` publica; no envía correos, no decide canales, no escribe en el almacenamiento.

La prueba de que se respeta: el gateway de notificaciones existe precisamente porque la alternativa era que el caso de uso supiera de tres proveedores. Y el proceso de la bandeja despacha, pero **no decide el contenido** del aviso: eso es del gateway.

Donde se nota el límite: un endpoint traduce HTTP y nada más. Si un endpoint tuviera una regla de negocio, la segunda ruta que hiciera lo mismo la duplicaría.

### O — Abierto/cerrado

**Añadir un canal de notificación no modifica ningún módulo emisor:** es un adaptador nuevo y una fila en la política. Lo mismo con un proveedor de correo distinto, o con una caché distribuida.

El mecanismo que lo hace posible es que la extensión ocurre **detrás de un puerto**. Sin los puertos, cada canal nuevo tocaría cada emisor.

### L — Sustitución de Liskov

Los tres adaptadores del puerto de correo son sustituibles: simulado, captura y real. La prueba de integración del flujo completo usa el simulado y **vale igual**, porque ninguno cambia el contrato.

Donde esto se cuida de verdad: un adaptador no puede lanzar excepciones que el puerto no declare ni cambiar el significado del resultado. Por eso los adaptadores devuelven un resultado explícito de envío (enviado, transitorio, permanente) en lugar de lanzar excepciones distintas cada uno.

### I — Segregación de interfaces

Los puertos de salida son **pequeños y por capacidad**: `IEnviadorCorreo`, `IWhatsAppGateway`, `IEnviadorPush`, `IAlmacenObjetos`. No hay un `IServiciosExternos` con quince métodos.

Beneficio concreto: una prueba de un caso de uso simula **solo** el puerto que ese caso de uso usa. Con una interfaz gorda habría que simular catorce métodos irrelevantes en cada prueba, y nadie lo haría.

### D — Inversión de dependencias

Es el principio que estructura toda la solución. **Las interfaces se declaran donde se usan, no donde se implementan**: los puertos de repositorio están en el dominio, los de salida en la aplicación, y la infraestructura los implementa.

Es lo que permite que el dominio no sepa que existe PostgreSQL, y es verificable: las pruebas de arquitectura comprueban que ni el dominio ni la aplicación referencien tecnología de persistencia (regla 5 de ADR-0001).

## 4. Clean Code

El enunciado pide evidenciar nombres expresivos, métodos cohesivos, responsabilidades claras, bajo acoplamiento, alta cohesión y código mantenible. Lo que sigue son las decisiones concretas, no la lista de deseos.

### Nombres

**El código está en español**, como el dominio y como el estilo del curso. Un dominio en español con código en inglés obliga a traducir mentalmente en cada lectura, y las traducciones a medias son peores que cualquiera de las dos opciones.

| Decisión | Ejemplo |
|---|---|
| Los casos de uso se nombran por la intención | `PublicarKudoCasoUso`, `AprobarSolicitudCasoUso`, `VetarContenidoCasoUso` |
| Las acciones de auditoría usan un vocabulario cerrado | `kudo.publicar`, `contenido.vetar`, `usuario.cambiar_rol`. Un nombre escrito a mano en cada sitio acaba teniendo cuatro variantes |
| Los nombres de las pruebas son frases que describen el comportamiento | `El_conflicto_de_concurrencia_lo_captura_el_manejador_de_errores_de_dominio` |
| Sin abreviaturas ni prefijos de tipo | `destinatario`, no `destUsr` |

El `.editorconfig` hace una excepción deliberada: en las pruebas se permite el guion bajo en los nombres, porque una frase legible vale más que la convención de mayúsculas.

### Métodos y funciones

- Un método hace una cosa y su nombre lo dice. Si hace falta un comentario para explicar **qué** hace, el nombre está mal.
- **El tiempo entra siempre como parámetro.** El dominio nunca consulta el reloj del sistema, así que las reglas son deterministas en las pruebas. Es una regla pequeña con un efecto grande: sin ella, probar «la calificación venció» exige esperar.
- Las entidades **validan sus invariantes en el constructor**, de modo que no exista una instancia inválida. Un Kudo cuyo autor es su propio destinatario no se puede construir.

### Comentarios

La regla es: **el comentario explica el por qué, nunca el qué**. Los que se escriben son los que un lector no puede deducir del código:

- Por qué el evento se marca procesado **dentro** de la misma transacción (hacerlo después reabre la ventana de pérdida).
- Por qué los campos estáticos de solo lectura están exentos de la regla del guion bajo (un valor fijo no es estado mutable).
- Por qué los argumentos de MSBuild están en un script de shell y no en PowerShell (PowerShell parte las cadenas con coma).
- Por qué la regla 4 de arquitectura es la única que el compilador no puede garantizar.

### Lo que hace cumplir esto automáticamente

Clean Code no se sostiene con buena voluntad. El pipeline lo verifica (ADR-0017):

| Mecanismo | Qué impone |
|---|---|
| `TreatWarningsAsErrors` | Una advertencia aceptada una vez se acepta siempre |
| `dotnet format --verify-no-changes` | Estilo uniforme que no hay que discutir en ninguna revisión |
| Reglas de nomenclatura en `.editorconfig` como **error** | Nombres consistentes |
| `IDE0005` como error | Nada de directivas `using` sin usar acumulándose |
| `IDE0130` como error | El espacio de nombres sigue a la carpeta: un archivo movido no queda con el nombre viejo |
| `file_scoped namespace` obligatorio | Un nivel de indentación menos en cada archivo |
| Campos de solo lectura por omisión | Inmutabilidad como posición de partida |
| **Pruebas de arquitectura** | Bajo acoplamiento y alta cohesión, verificados y no afirmados |

La última es la importante. «Bajo acoplamiento» es una afirmación que cualquier documento puede hacer; una prueba que falla cuando el dominio referencia infraestructura es evidencia.

## 5. Dónde ver cada cosa

| Qué | Dónde |
|---|---|
| Repository, Unit of Work | `UcoKudos.Domain/*/Repositorios`, `UcoKudos.Infrastructure/Persistencia` |
| Transactional Outbox | `UcoKudos.Infrastructure/Persistencia/Outbox`, `UcoKudos.Infrastructure/Notificaciones` |
| Adapter, Strategy, Mediator | `UcoKudos.Application/Notificaciones`, `UcoKudos.Infrastructure/Canal` |
| Ports and Adapters | La estructura de `UcoKudos.Application/<modulo>/{Entrada,Salida}` |
| Cache-Aside, Materialized View | `UcoKudos.Application/Lectura`, `UcoKudos.Infrastructure/Lectura` |
| Materialized Path | `UcoKudos.Domain/Interaccion/RutaReplica` |
| Archivos por la API (antes Valet Key) | `UcoKudos.Application/Kudos/CasosUso/AdjuntosCasosUso`, `UcoKudos.Infrastructure/Objetos` |
| SOLID verificado | `tests/UcoKudos.ArchitectureTests` |
| Clean Code verificado | `.editorconfig`, `Directory.Build.props` y el pipeline |
