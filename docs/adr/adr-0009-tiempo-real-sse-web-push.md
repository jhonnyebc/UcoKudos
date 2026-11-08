# ADR-0009: notificación en tiempo real con SSE para usuarios conectados y Web Push para los desconectados

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0009 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB del canal push del ABB de Notificaciones |

## Contexto

RF-16 exige una «notificación PUSH en tiempo real si el destinatario se encuentra conectado a la aplicación». Leído al pie de la letra, basta con avisar a quien tiene la aplicación abierta. Pero el enunciado también lista «Push Notifications» entre las consideraciones arquitectónicas y pide evidencia de su funcionamiento, y el 90 % de los usuarios está en un celular (DRV-001), donde la aplicación casi nunca está abierta en el momento en que llega el Kudo.

Las dos tecnologías disponibles resuelven mitades distintas del problema:

- **SSE** (eventos enviados por el servidor) mantiene una conexión HTTP abierta mientras la página está cargada. Es trivial de implementar, no necesita permisos del usuario y entrega al instante. No sirve si la aplicación está cerrada.
- **Web Push** con el protocolo VAPID entrega a través del servicio de mensajería del navegador, incluso con la aplicación cerrada y el celular bloqueado. Exige un *service worker*, el permiso explícito del usuario y guardar su suscripción.

## Decisión

Implementar **los dos mecanismos**, como dos adaptadores del mismo puerto del gateway (ADR-0006), con una política de preferencia explícita.

1. **SSE para la sesión activa.** Un punto de acceso de eventos por usuario autenticado. Si el destinatario tiene la aplicación abierta, recibe el aviso al instante y la interfaz lo muestra sin recargar.
2. **Web Push con VAPID para el resto.** Si el usuario concedió permiso de notificaciones, su suscripción queda guardada y el aviso llega aunque la aplicación esté cerrada. Las claves VAPID se generan una vez; la privada es un secreto (ADR-0018).
3. **Política de preferencia:** si hay una conexión SSE activa para ese usuario, se entrega por SSE y **no** se envía Web Push, para no notificar dos veces lo mismo. Si no la hay y existe suscripción, se envía Web Push. Si no hay ninguna de las dos, el aviso queda guardado y se ve al entrar: ninguna notificación se pierde, solo cambia su inmediatez.
4. **Los avisos se persisten siempre**, además de enviarse. Un canal en tiempo real es un canal efímero; un listado de avisos con estado de leído es lo que permite que el usuario no se pierda nada y que la entrega tenga evidencia consultable.
5. **El permiso se pide en el momento oportuno**, no al abrir la aplicación por primera vez: se pide después de que el usuario publica o recibe su primer Kudo, cuando el valor de la notificación es evidente. Un permiso pedido en frío se deniega, y una vez denegado no se puede volver a pedir.
6. **Las suscripciones caducadas se eliminan.** Cuando el servicio del navegador responde que una suscripción ya no es válida, se borra. Una tabla de suscripciones muertas genera fallos permanentes en cada publicación.
7. **El *service worker* es el de la PWA** de ADR-0015, que sirve también para la instalación en la pantalla de inicio.

## Alternativas consideradas

| Criterio | **SSE + Web Push (elegida)** | A. Solo SSE | B. Solo Web Push | C. WebSocket | D. Firebase Cloud Messaging |
|---|---|---|---|---|---|
| Cumple RF-16 al pie de la letra | Cumple | Cumple | Cumple | Cumple | Cumple |
| Llega con la aplicación cerrada (DRV-001) | Cumple | **No cumple** | Cumple | No cumple | Cumple |
| No requiere permiso del usuario | Parcial — SSE no, Web Push sí | Cumple | No cumple | Cumple | No cumple |
| Esfuerzo (RN-02) | Parcial — dos adaptadores | Cumple | Parcial | Parcial | Parcial — SDK y proyecto externo |
| Comunicación bidireccional | No necesaria | No | No | Cumple — sin driver que lo pida | No |
| Dependencias externas (RT-07) | Cumple — ninguna | Cumple | Cumple — el servicio del navegador es gratuito | Cumple | **No cumple** — ata a un servicio de terceros |
| Calidad de la evidencia a sustentar | Cumple — notificación en el celular bloqueado | Parcial — solo con la página abierta | Cumple | Parcial | Cumple |

- **A. Solo SSE** — descartada: cumple la letra de RF-16 y es lo más rápido, pero en un escenario 90 % móvil notifica casi a nadie, y la evidencia a mostrar sería pobre.
- **B. Solo Web Push** — descartada: es el mecanismo más potente, pero depende de un permiso que el usuario puede denegar. Sin SSE, un usuario que denegó el permiso no recibiría nada **estando conectado**, que es exactamente el caso que RF-16 exige cubrir.
- **C. WebSocket** — descartada: ofrece comunicación bidireccional que ningún requisito necesita, y su mantenimiento de conexión es más complejo que el de SSE. Para enviar del servidor al cliente, SSE es la herramienta del tamaño del problema.
- **D. Firebase Cloud Messaging** — descartada: resolvería lo mismo atando la solución a un servicio de terceros, cuando Web Push con VAPID es un estándar abierto que los navegadores implementan de forma nativa.

## Consecuencias

**Positivas**

- El requisito queda cubierto en los dos escenarios reales: con la aplicación abierta y con el celular bloqueado.
- La evidencia para la entrega es fuerte: se puede mostrar la notificación apareciendo en un celular bloqueado.
- No hay dependencia de ningún servicio de terceros: VAPID es un estándar y SSE es HTTP.
- Los avisos persistidos hacen que el sistema sea correcto incluso si fallan los dos canales.

**Negativas**

- **Dos mecanismos que mantener y explicar**, con la lógica de preferencia entre ellos. Es el precio de cubrir los dos escenarios.
- Web Push depende del permiso del usuario, que puede denegar y que luego no se puede volver a pedir.
- SSE mantiene una conexión abierta por usuario conectado, lo que consume un hilo o una conexión del servidor. Con el volumen de una universidad es asumible; con más, sería un motivo para revisar.
- Saber si un usuario «está conectado» es un estado en memoria del proceso. Con varias instancias, cada una conoce solo sus conexiones, así que la política de preferencia habría que coordinarla. Hoy hay una sola instancia; queda anotado como punto de revisión.

## Tácticas de Bass aplicadas

- **Mantener múltiples copias de la computación** — dos canales cubren el mismo requisito en entornos distintos.
- **Degradación elegante** — si no hay canal en vivo, el aviso queda persistido.
- **Reducir acoplamiento** — los dos son adaptadores del mismo puerto.
- **Detectar fallos** — las suscripciones inválidas se depuran automáticamente.

## Escenarios y requisitos relacionados

- `RF-16` — el requisito que este ADR responde.
- `QA-CONF-01` — el aviso persistido garantiza que nada se pierde.
- `QA-MANT-01` — los dos canales entran y salen como adaptadores.
- `DRV-001`, `DRV-003`.

## ADRs relacionados

- **ADR-0006** — el gateway que aplica la política de preferencia.
- **ADR-0015** — la PWA que aporta el *service worker*.
- **ADR-0018** — dónde vive la clave privada VAPID.
- **ADR-0005** — la bandeja desde la que se despachan los avisos.

## Notas de implementación

- La clave pública VAPID la necesita el cliente y **no** es un secreto; la privada sí. Confundirlas es el error típico: publicar la privada permitiría a cualquiera enviar notificaciones a los usuarios.
- El *service worker* de la PWA puede perder la suscripción al actualizarse. Hay que volver a comprobar y renovar la suscripción al arrancar la aplicación, no solo la primera vez.
- La conexión SSE se corta sola en redes móviles. El cliente debe reconectar, y el servidor enviar un comentario periódico para que los intermediarios no cierren la conexión por inactividad.
- Las notificaciones no llevan el contenido del Kudo en el cuerpo del mensaje push más allá de lo necesario: una notificación aparece en la pantalla de bloqueo, donde la puede leer cualquiera que tenga el teléfono a la vista.

## Referencias

- Thomson, M., Damaggio, E. y Raymor, B. *Voluntary Application Server Identification (VAPID) for Web Push*, RFC 8292.
- Thomson, M. y Beverloo, P. *Generic Event Delivery Using HTTP Push*, RFC 8030.
- WHATWG. *HTML Living Standard — Server-sent events.*
- W3C. *Push API* y *Service Workers.*
