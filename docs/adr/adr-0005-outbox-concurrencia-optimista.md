# ADR-0005: Transactional Outbox y concurrencia optimista para los efectos de publicar un Kudo

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0005 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Mensajería y Confiabilidad |

## Contexto

Publicar un Kudo (RF-07) debe producir cinco efectos: persistir el reconocimiento, enviar un correo, enviar un WhatsApp, notificar en tiempo real si el destinatario está conectado y dejar el proceso auditado.

Los tres canales son servicios externos, lentos y falibles. Esto plantea dos problemas clásicos:

1. **Si se envían dentro de la transacción**, el usuario espera a que respondan tres proveedores, y un 503 de Meta haría fallar la publicación de un Kudo que no tiene nada de malo. Eso contradice QA-DISP-01.
2. **Si se envían después de confirmar la transacción**, existe la ventana «commit exitoso, envío perdido»: si el proceso se cae justo después de confirmar, el Kudo queda publicado y nadie se enteró. Eso contradice QA-CONF-01.

Además el enunciado exige justificar el **Manifiesto de Sistemas Reactivos** (RT-06), cuyos cuatro rasgos —responsividad, resiliencia, elasticidad y orientación a mensajes— se juegan precisamente aquí.

## Decisión

Usar el patrón **Transactional Outbox** sobre PostgreSQL, con **concurrencia optimista** en los agregados que compiten.

1. **Una sola transacción** escribe el Kudo, su evento de auditoría y un registro en la **bandeja de salida** (una tabla de eventos pendientes). Los tres se confirman juntos o ninguno. El usuario recibe la respuesta en cuanto eso ocurre: no espera a ningún proveedor externo (responsividad).
2. **Un proceso en segundo plano** toma los eventos pendientes y produce las notificaciones. Toma cada evento con `SELECT ... FOR UPDATE SKIP LOCKED`, de modo que si hay varias instancias de la API, ninguna procesa el mismo evento que otra (elasticidad, QA-ELAS-01).
3. **Reintentos con espera creciente.** Un error transitorio devuelve el evento a la cola con un próximo intento más lejano. Tras un número máximo de intentos, el evento se descarta y queda registrado el fallo (resiliencia).
4. **Idempotencia obligatoria en los consumidores.** Cada notificación se identifica por evento y destinatario, así que reprocesar un evento no produce un segundo correo ni un segundo WhatsApp (QA-CONF-02).
5. **Eventos programados.** La bandeja admite un instante de próximo intento en el futuro, así que un recordatorio no necesita un planificador aparte.
6. **Concurrencia optimista** con un token de versión en los agregados susceptibles de edición simultánea. Si dos operaciones compiten, solo una confirma; la otra recibe un conflicto explicable al usuario en lugar de sobrescribir en silencio.
7. **Sin broker de mensajes.** La cola es una tabla de PostgreSQL.

## Alternativas consideradas

| Criterio | **Outbox en PostgreSQL (elegida)** | A. Envío síncrono en la transacción | B. Envío tras confirmar, sin bandeja | C. Outbox + RabbitMQ o Kafka |
|---|---|---|---|---|
| Ningún evento se pierde (QA-CONF-01) | Cumple | Cumple | **No cumple** — ventana de pérdida tras el commit | Cumple |
| Publicar no depende de los proveedores (QA-DISP-01) | Cumple | **No cumple** — un 503 de Meta rompe la publicación | Cumple | Cumple |
| Responsividad percibida (DRV-001) | Cumple — responde al confirmar | No cumple — el usuario espera tres servicios | Cumple | Cumple |
| Sin duplicados al reintentar (QA-CONF-02) | Cumple — deduplicación por evento | No aplica | No cumple | Cumple |
| Varias instancias sin pisarse (QA-ELAS-01) | Cumple — `SKIP LOCKED` | No aplica | No cumple | Cumple |
| Piezas que operar y escanear (RT-07, RN-02) | Cumple — ninguna nueva | Cumple | Cumple | **No cumple** — un contenedor más en una máquina gratuita |
| Orden y reparto a muchos consumidores | Parcial — suficiente para un consumidor | No aplica | No aplica | Cumple — sin driver que lo pida |

- **A. Envío síncrono** — descartada: viola QA-DISP-01 y castiga la responsividad, que es justo lo que DRV-001 exige en red móvil.
- **B. Envío tras confirmar sin bandeja** — descartada: es el camino que parece obvio y es el que pierde notificaciones. Es el error que el patrón Outbox existe para evitar.
- **C. Outbox con un broker** — descartada **para esta línea base**: es la evolución natural, pero hoy no hay ningún driver que la pida. Añade un contenedor que consume memoria de la única máquina gratuita, otro componente que escanear con Trivy y otra pieza que explicar. La bandeja queda detrás de un puerto, así que sustituir el transporte más adelante no toca el dominio.

## Consecuencias

**Positivas**

- La publicación responde rápido y no depende de que Meta, el proveedor de correo o el navegador del destinatario estén disponibles.
- Ninguna notificación se pierde ni se duplica, y eso es demostrable con una prueba que mata el proceso entre el commit y el envío.
- El `SKIP LOCKED` deja la puerta abierta a escalar horizontalmente sin cambiar nada.
- Los cuatro rasgos del Manifiesto Reactivo quedan sostenidos por un mecanismo concreto y no por una afirmación.

**Negativas**

- Las notificaciones son **eventualmente consistentes**: hay un retardo de segundos entre publicar y que llegue el correo. Es aceptable y hay que decirlo en la sustentación.
- Una tabla y un proceso en segundo plano más que operar y vigilar.
- Todos los consumidores deben ser idempotentes; es una disciplina que hay que respetar en cada canal nuevo.
- Usar la base como cola no escala indefinidamente. Es suficiente para el volumen de una universidad y el punto de cambio está identificado.

## Tácticas de Bass aplicadas

- **Mantener la consistencia de los datos** — una transacción cubre el cambio, su auditoría y su evento.
- **Encolar** — los eventos pendientes desacoplan el commit del envío externo.
- **Reintentar** — con espera creciente ante errores transitorios.
- **Controlar la concurrencia** — el token de versión detecta escrituras competidoras.
- **Detectar fallos** — los eventos descartados quedan registrados y medidos.

## Escenarios y requisitos relacionados

- `QA-CONF-01` — ninguna notificación se pierde si el proceso se cae tras confirmar.
- `QA-CONF-02` — reprocesar un evento no duplica mensajes.
- `QA-DISP-01` — Meta caída no impide publicar.
- `QA-ELAS-01` — dos instancias no duplican envíos.
- `RF-07`, `RF-14`, `RF-15`, `RF-16`, `RF-17`, `DRV-003`, `RT-06`.

## ADRs relacionados

- **ADR-0003** — la base que provee la transacción y la tabla de la bandeja.
- **ADR-0006** — el gateway que consume estos eventos.
- **ADR-0013** — la auditoría que se confirma en la misma transacción.

## Notas de implementación

- El proceso marca el evento como procesado **dentro de la misma transacción** en que lo atiende: marcarlo después abre otra vez la ventana de pérdida que el patrón vino a cerrar.
- La prueba decisiva no es que el correo llegue, sino que llegue **una sola vez** tras forzar un reproceso, y que llegue **igual** tras matar el proceso entre el commit y el envío. Esas dos pruebas son la evidencia de QA-CONF-01 y QA-CONF-02.
- Se miden tres cosas: eventos pendientes, antigüedad del más viejo y eventos descartados. Una bandeja que crece es el primer síntoma de que un proveedor dejó de responder.
- Los errores se clasifican en transitorios y permanentes antes de decidir el reintento: reintentar un número de teléfono inválido mil veces no lo vuelve válido.

## Referencias

- Richardson, C. *Pattern: Transactional Outbox.* microservices.io.
- Reactive Manifesto. *The Reactive Manifesto v2.0.* reactivemanifesto.org.
- PostgreSQL Global Development Group. *Documentation — SELECT FOR UPDATE SKIP LOCKED.*
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice.*
