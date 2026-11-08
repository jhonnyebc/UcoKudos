# ADR-0006: centralizar las notificaciones en un gateway multicanal con un adaptador por canal

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0006 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Notificaciones |

## Contexto

DRV-003 exige tres canales: correo, WhatsApp y push en tiempo real. Publicar un Kudo debe usar los tres a la vez (RF-07), y otros flujos usan solo alguno: la verificación del celular usa WhatsApp, la aprobación de una solicitud usa correo, el veto de un contenido avisa al autor.

Cada canal impone sus propias reglas. WhatsApp obliga a usar una plantilla aprobada fuera de la ventana de conversación (ADR-0007). El correo tiene un límite diario en la capa gratuita (ADR-0008). El push solo sirve si el destinatario tiene una suscripción vigente (ADR-0009). Los tres necesitan reintentos, deduplicación y clasificación de errores.

Si cada caso de uso llamara directamente a cada canal, esas reglas se repetirían en cada módulo, y añadir un canal obligaría a tocar todos los emisores. Además los textos quedarían incrustados en el código.

## Decisión

Un módulo **Notificaciones** con un **puerto de entrada único** que los demás módulos invocan, y un **adaptador de salida por canal**.

1. **Contrato único.** El puerto recibe una solicitud de notificación con: destinatario (identificador interno, no un correo ni un teléfono), tipo de notificación, clave del mensaje, variables e identificador del evento de origen.
2. **El emisor expresa intención, no canal.** El caso de uso «publicar Kudo» no sabe que existen tres canales: deposita un evento (ADR-0005) y el gateway decide. Esto es el patrón **Mediator** en la frontera y **Strategy** en la selección de canal.
3. **Política de canal por tipo de notificación y destinatario.** Para un Kudo publicado: correo + WhatsApp + push al destinatario. Para una solicitud aprobada: correo. Para la verificación de celular: WhatsApp. La política es una tabla explícita, no una cadena de condicionales dispersa.
4. **Catálogo de mensajes externo al código**, en un recurso con los textos en español de Colombia y sus variables. Cambiar una redacción no es cambiar lógica. Cada clave de mensaje declara qué variables espera, y una prueba verifica que el catálogo y el código no se desincronicen.
5. **Independencia entre canales.** Si WhatsApp falla, el correo y el push se envían igual. Un canal caído degrada el servicio, no lo interrumpe: cada canal se registra y se reintenta por separado.
6. **Deduplicación por evento y destinatario**, de modo que un reproceso de la bandeja no genere mensajes repetidos (QA-CONF-02).
7. **Un canal nuevo es un adaptador nuevo** más una fila en la política. Ningún módulo emisor cambia (QA-MANT-01).

## Alternativas consideradas

| Criterio | **Gateway con política de canal (elegida)** | A. Cada caso de uso llama a cada canal | B. Plataforma de notificaciones externa | C. Un solo canal y los demás después |
|---|---|---|---|---|
| Reglas de cada canal en un solo lugar | Cumple | **No cumple** — se repiten en cada módulo | Cumple | Parcial |
| Añadir un canal sin tocar emisores (QA-MANT-01) | Cumple | No cumple | Cumple | No cumple |
| Deduplicación y reintentos centralizados | Cumple | No cumple | Parcial | Parcial |
| Un canal caído no tumba los otros (QA-DISP-01) | Cumple | Parcial — depende de cada emisor | Cumple | No aplica |
| Costo (RT-07) | Cumple | Cumple | **No cumple** — suscripción o margen por mensaje | Cumple |
| Cumple DRV-003 | Cumple | Cumple | Cumple | **No cumple** |
| Textos editables sin desplegar código | Cumple — catálogo | No cumple | Cumple | Parcial |

- **A. Llamada directa desde cada caso de uso** — descartada: es lo más rápido de escribir el primer día y lo más caro a partir del segundo. Duplica reglas de ventana de WhatsApp, reintentos y deduplicación en cada módulo.
- **B. Plataforma externa de notificaciones** — descartada: resolvería el problema, pero añade un intermediario, un costo y una dependencia sin ningún driver que lo justifique, cuando los tres canales se integran directamente sin dificultad.
- **C. Entregar un canal y dejar los otros para después** — descartada: DRV-003 y RF-07 exigen los tres funcionando el día de la entrega, con evidencia.

## Consecuencias

**Positivas**

- Las reglas de cada proveedor, la idempotencia y los reintentos se escriben una sola vez.
- Los módulos de negocio quedan limpios de detalles de mensajería, lo que es evidencia directa de bajo acoplamiento y responsabilidad única.
- Los textos viven en un catálogo, así que corregir una redacción en la sustentación no exige recompilar.
- La prueba de un caso de uso no necesita simular tres proveedores: simula un puerto.

**Negativas**

- Es un módulo más, con su política y su catálogo que mantener de acuerdo.
- Una indirección añadida: para saber qué le llega a un usuario hay que leer la política, no el caso de uso. Se mitiga documentando la política como una tabla en el documento de arquitectura.
- El catálogo fuera del código puede desincronizarse de las variables que el código pasa; la prueba del punto 4 lo detecta.

## Tácticas de Bass aplicadas

- **Usar un intermediario** — los emisores no conocen el canal.
- **Reducir acoplamiento** — un adaptador por proveedor detrás de un puerto.
- **Reintentar** y **encolar** — sobre la bandeja de ADR-0005.
- **Detectar fallos** — cada envío fallido se registra y se mide por canal.
- **Degradación elegante** — un canal caído no cancela los demás.

## Escenarios y requisitos relacionados

- `QA-MANT-01` — cambiar de proveedor es un adaptador nuevo.
- `QA-CONF-02` — sin duplicados al reprocesar.
- `QA-DISP-01` — independencia entre canales.
- `RF-07`, `RF-14`, `RF-15`, `RF-16`, `DRV-003`.

## ADRs relacionados

- **ADR-0005** — la bandeja de eventos que este gateway consume.
- **ADR-0007** — el adaptador de WhatsApp.
- **ADR-0008** — el adaptador de correo.
- **ADR-0009** — el adaptador de notificación en tiempo real.
- **ADR-0013** — el registro de auditoría de cada envío.

## Notas de implementación

- El gateway resuelve el destinatario a partir del identificador interno, nunca recibe un correo ni un teléfono desde el módulo emisor: así una falla de autorización en un módulo no se convierte en un envío a la dirección equivocada.
- Para las pruebas existe un adaptador simulado por canal que registra lo enviado en memoria. Eso permite que las pruebas de integración verifiquen el flujo completo de RF-07 sin tocar la red, y que la demostración se pueda ensayar sin consumir cuota.
- Cada canal registra el resultado del envío con el identificador del evento, de modo que la evidencia que pide el enunciado (correo, WhatsApp y push funcionando) se pueda mostrar desde la propia plataforma y no solo desde el celular.

## Referencias

- Gamma, E. *et al.* *Design Patterns* — Mediator, Strategy, Adapter.
- Hohpe, G. y Woolf, B. *Enterprise Integration Patterns* — Messaging Gateway, Message Router.
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice.*
