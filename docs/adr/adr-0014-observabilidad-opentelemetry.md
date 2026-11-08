# ADR-0014: observabilidad con OpenTelemetry y los tres pilares unidos por el identificador de traza

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0014 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Observabilidad y Logging |

## Contexto

DRV-006 exige capacidades de monitoreo, métricas, trazas y auditoría, y la observabilidad es una evidencia obligatoria de la entrega. La auditoría se resuelve aparte (ADR-0013); aquí están los otros tres.

El flujo que hay que poder observar es el de ADR-0005: una petición publica un Kudo, confirma una transacción y deja un evento; **otro proceso**, segundos después y en otro hilo, produce tres notificaciones a tres servicios externos. Si algo sale mal, la pregunta no es «¿hubo un error?», sino «¿qué le pasó a **esta** publicación concreta?».

Responder eso exige que los logs del proceso en segundo plano se puedan unir con los de la petición que originó el evento. Sin un identificador que los enlace, son dos montones de texto sin relación.

El presupuesto es cero (RT-07) y la máquina es una sola (ADR-0016), lo que descarta una plataforma de observabilidad de pago.

## Decisión

Instrumentar con **OpenTelemetry** desde la línea base, exportar por su protocolo estándar a un recolector autoalojado, y **unir los tres pilares con el identificador de traza**.

1. **OpenTelemetry como estándar de instrumentación**, no el SDK de un proveedor. El código se instrumenta una vez; a dónde se envían los datos es configuración. Cambiar de destino no toca el código (QA-MANT-01).
2. **Trazas.** Cada petición abre una traza. Se instrumentan las peticiones HTTP entrantes, las consultas a la base, las llamadas HTTP salientes a los proveedores y las vueltas del proceso de notificaciones.
3. **La traza cruza la frontera asíncrona.** Al escribir un evento en la bandeja se guarda el contexto de traza de la petición que lo originó; al procesarlo, el proceso en segundo plano lo retoma y continúa esa misma traza. **Esta es la decisión central del ADR**: sin ella, la publicación y sus notificaciones serían trazas inconexas y QA-OBS-01 no se podría cumplir.
4. **Logs estructurados en JSON**, cada línea con el identificador de traza. Nunca texto libre concatenado: un log que hay que leer con expresiones regulares no se puede consultar.
5. **Métricas.** Las técnicas habituales (latencia por ruta, tasa de error, uso de recursos) y además las **métricas de negocio que importan aquí**: eventos pendientes en la bandeja, antigüedad del evento más viejo, envíos fallidos por canal y eventos descartados. Una bandeja que crece es el primer síntoma de que un proveedor dejó de responder, y es lo que hay que vigilar.
6. **Puntos de salud** separados: uno de vida, que responde si el proceso está en pie, y uno de preparación, que comprueba la base y el almacenamiento de objetos. El de preparación es el que decide si la instancia debe recibir tráfico.
7. **El identificador de traza se devuelve al cliente** en las respuestas de error, con el formato estándar de problema HTTP. Así un usuario puede reportar un error con un identificador y se encuentra la traza exacta.
8. **Recolector autoalojado** en la misma máquina, en un contenedor, con su interfaz accesible solo por un túnel y no publicada en internet: muestra datos internos del sistema.
9. **Nada de datos personales en logs ni en trazas.** El texto de un Kudo, un correo o un teléfono no se registran. Los logs se diagnostican con identificadores, no con contenido.

## Alternativas consideradas

| Criterio | **OpenTelemetry + recolector autoalojado (elegida)** | A. Solo logs en archivo | B. Plataforma de observabilidad gestionada | C. SDK propietario de un proveedor | D. Métricas con Prometheus, sin trazas |
|---|---|---|---|---|---|
| Seguir una publicación de punta a punta (QA-OBS-01) | Cumple — traza propagada al proceso asíncrono | **No cumple** | Cumple | Cumple | **No cumple** — sin trazas |
| Los tres pilares unidos | Cumple — identificador de traza en logs y trazas | No cumple | Cumple | Cumple | No cumple |
| Costo (RT-07) | Cumple | Cumple | **No cumple** — capa gratuita insuficiente o de pago | Parcial | Cumple |
| Independencia del destino (QA-MANT-01) | Cumple — estándar abierto | Cumple | Parcial | **No cumple** | Cumple |
| Esfuerzo (RN-02) | Cumple — instrumentación automática en su mayor parte | Cumple | Parcial | Parcial | Cumple |
| Calidad de la evidencia a sustentar | Cumple — se muestra la traza completa | Parcial | Cumple | Cumple | Parcial |

- **A. Solo logs en archivo** — descartada: es lo que uno tiene por omisión y no responde la única pregunta que importa en un flujo asíncrono. Habría que correlacionar a mano entre dos procesos.
- **B. Plataforma gestionada** — descartada por RT-07: sus capas gratuitas retienen datos pocos días o limitan el volumen, y depender de ellas el día de la sustentación es un riesgo innecesario.
- **C. SDK propietario** — descartada: ataría el código a un destino concreto, justo lo que el estándar evita.
- **D. Solo métricas** — descartada: las métricas dicen *que* algo va mal y en qué proporción, pero no *qué le pasó a esta publicación*. Para eso hacen falta trazas.

## Consecuencias

**Positivas**

- Se puede reconstruir el recorrido completo de una publicación, incluido lo que ocurrió en el proceso en segundo plano, con un solo identificador.
- Las métricas de la bandeja de eventos dan aviso temprano de que un canal externo está fallando, antes de que alguien se queje.
- Un usuario puede reportar un error con un identificador que lleva directo a la traza.
- Instrumentado con un estándar abierto, así que llevar los datos a otro destino es configuración.

**Negativas**

- La instrumentación tiene un costo en rendimiento, pequeño pero real. Se controla con muestreo si llegara a notarse; en este volumen no hace falta.
- El recolector es un contenedor más que consume memoria de la única máquina y que hay que escanear.
- El recolector autoalojado elegido no persiste los datos al reiniciarse, así que las trazas son para diagnosticar en el momento, no un archivo histórico. El archivo histórico es la auditoría (ADR-0013), que sí persiste.
- Propagar el contexto a través de la bandeja es código que hay que escribir y que es fácil olvidar al añadir un tipo de evento nuevo.

## Tácticas de Bass aplicadas

- **Monitorear** — métricas, trazas y puntos de salud.
- **Detectar fallos** — señales sobre la bandeja y los envíos.
- **Auditar** — complementada por ADR-0013.
- **Reducir acoplamiento** — instrumentación estándar, destino configurable.

## Escenarios y requisitos relacionados

- `QA-OBS-01` — reconstruir el flujo completo de una publicación concreta.
- `QA-REND-01` y `QA-REND-02` — las métricas de latencia son lo que verifica estos escenarios.
- `RF-18`, `DRV-006`.

## ADRs relacionados

- **ADR-0005** — la frontera asíncrona que la traza debe cruzar.
- **ADR-0013** — la auditoría, que es el registro permanente.
- **ADR-0016** — la máquina donde corre el recolector.
- **ADR-0017** — el pipeline, donde se verifica que la instrumentación sigue en pie.

## Notas de implementación

- La prueba de que esto funciona es un ejercicio concreto: publicar un Kudo, tomar el identificador de traza de la respuesta y encontrar en una sola vista la petición, la transacción, el evento y los tres envíos. Si eso no se puede hacer, la propagación de contexto está mal y el requisito no está cumplido.
- Los puntos de salud no deben exigir autenticación, porque los consulta la infraestructura, pero tampoco deben revelar detalles internos: responden si el sistema está listo, no por qué no lo está.
- La revisión de que no se registran datos personales se hace leyendo los logs de una publicación real antes de la entrega. Es el tipo de fuga que se descubre mirando, no razonando.
- Las métricas de negocio se nombran con un prefijo propio de la plataforma para distinguirlas de las que genera la instrumentación automática.

## Referencias

- OpenTelemetry Authors. *OpenTelemetry Specification* y *Context Propagation.*
- Nottingham, M. *Problem Details for HTTP APIs*, RFC 9457.
- Sridharan, C. *Distributed Systems Observability.*
- OWASP. *Logging Cheat Sheet.*
