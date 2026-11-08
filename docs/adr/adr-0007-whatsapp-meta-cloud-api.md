# ADR-0007: WhatsApp con Meta Cloud API, solo de salida y por plantilla aprobada

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0007 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB del canal WhatsApp del ABB de Notificaciones |

## Contexto

RF-15 exige que el destinatario de un Kudo reciba un mensaje de WhatsApp, y la evidencia de ese envío es obligatoria en la entrega. RF-01 pide además un número celular en el registro, que hay que verificar (RF-02).

Tres hechos de la plataforma de Meta condicionan el diseño:

1. **Ventana de conversación.** Un mensaje de texto libre solo se puede enviar dentro de las 24 horas siguientes a que el usuario escriba al negocio. El destinatario de un Kudo **nunca escribió primero**, así que la plataforma está **siempre fuera de la ventana**. Eso obliga a usar una **plantilla aprobada por Meta** en todos los envíos.
2. **Número de prueba.** La cuenta de desarrollador incluye un número de prueba gratuito que solo envía a una lista corta de destinatarios registrados en el portal (del orden de cinco).
3. **Tokens.** El token que el portal muestra por defecto caduca en horas; para un servicio hay que usar un token de usuario del sistema, sin caducidad.

El autor ya tiene cuenta de desarrollador de Meta y un adaptador de esta API construido y probado en un proyecto anterior, con la clasificación de errores ya resuelta.

## Decisión

Usar **WhatsApp Business Cloud API de Meta** con el **número de prueba**, detrás de un puerto de salida, **solo para mensajes de salida** y **siempre por plantilla**.

1. **Puerto de salida propio.** El dominio y los casos de uso no conocen Meta: conocen un puerto con las operaciones de enviar plantilla y consultar el resultado. Cambiar a otro proveedor es un adaptador nuevo (QA-MANT-01).
2. **Solo salida: no hay bot ni webhook.** Kudos no necesita recibir mensajes. Esto elimina el punto de entrada público, la verificación de firma del webhook, la bandeja de entrada idempotente y la necesidad de un túnel hacia el entorno local. Es la simplificación más grande que permite el enunciado y conviene señalarla: no se construye lo que no se necesita.
3. **Dos plantillas**, enviadas a aprobación al inicio del desarrollo porque la aprobación de Meta no es inmediata:
   - Aviso de Kudo recibido, con autor, categoría y un enlace para verlo.
   - Código de verificación del celular (RF-02).
4. **Clasificación de errores en dos clases.** Tiempo de espera agotado, límite de tasa, errores 5xx y fallas de red son **transitorios** y el evento vuelve a la cola con espera creciente. El resto son **permanentes**: el envío se marca fallido, se audita y no se reintenta. Reintentar un número inválido no lo vuelve válido.
5. **Adaptador simulado** del mismo puerto, que registra en memoria lo que se habría enviado. Es el que usan las pruebas automatizadas y el desarrollo sin red, para no consumir cuota ni depender de la disponibilidad de Meta en el pipeline.
6. **Teléfonos normalizados a E.164** antes de guardarlos, porque es el formato que exige la API. La normalización ocurre en el borde de entrada, no en el adaptador.
7. **El token es un secreto** y vive donde manda ADR-0018. Nunca en el repositorio.

## Alternativas consideradas

| Criterio | **Meta Cloud API, número de prueba (elegida)** | A. Twilio Sandbox | B. Número propio verificado en Meta | C. Biblioteca no oficial sobre WhatsApp Web |
|---|---|---|---|---|
| Costo (RT-07) | Cumple — número de prueba gratuito | Parcial — exige crédito y los mensajes se tarifan | **No cumple** — exige verificación de empresa | Cumple |
| Es la API oficial | Cumple | Cumple — como intermediario | Cumple | **No cumple** — viola los términos y puede bloquear la cuenta |
| Esfuerzo (RN-02) | Cumple — adaptador ya construido y probado | Parcial — adaptador nuevo | No cumple — la verificación tarda días | No cumple — frágil |
| Estabilidad durante la sustentación | Cumple — destinatarios registrados de antemano | **No cumple** — el destinatario debe enviar un código de unión y la sesión caduca a los 3 días | Cumple | No cumple |
| Destinatarios permitidos | Parcial — unos cinco registrados | Parcial — los que se hayan unido | Cumple — cualquiera | Cumple |
| Riesgo académico | Ninguno | Ninguno | Ninguno | **Alto** — cuenta bloqueada el día de la entrega |

- **A. Twilio Sandbox** — descartada principalmente por un riesgo de calendario: cada destinatario debe enviar un código de unión al número compartido y **la unión caduca a los tres días**. Si la sustentación se corre un día, la demostración deja de funcionar sin que nada falle en el código.
- **B. Número propio verificado** — descartada: es lo correcto en producción, pero la verificación de empresa de Meta tarda más que el plazo disponible. Queda documentada como la evolución natural.
- **C. Biblioteca no oficial** — descartada sin matices: incumple los términos de servicio y arriesga el bloqueo de la cuenta justo cuando hay que demostrar el flujo.

## Consecuencias

**Positivas**

- El canal más vistoso de la demostración funciona con la API oficial y sin costo.
- Al no haber webhook, la superficie de ataque es menor: no se expone ningún punto de entrada público para este canal.
- El adaptador simulado permite probar el flujo completo en el pipeline, sin red y sin cuota.
- Es sustituible: el puerto aísla a Meta del resto del sistema.

**Negativas**

- **Solo se puede enviar a los destinatarios registrados** (del orden de cinco). La demostración debe planearse con esos números y hay que decirlo al sustentar. En producción se resuelve con la alternativa B.
- **Dependencia de la aprobación de las plantillas.** Una plantilla rechazada bloquea el canal, y corregirla y reenviarla toma tiempo. Mitigación: enviarlas a aprobación el primer día.
- Las plantillas limitan la redacción: no se puede enviar texto arbitrario, así que el catálogo de mensajes de ADR-0006 debe respetar las variables aprobadas.
- Meta cambia su esquema de precios con el tiempo; el número de prueba evita el problema hoy, pero no es una garantía a futuro.

## Tácticas de Bass aplicadas

- **Reducir acoplamiento** — el proveedor queda detrás de un puerto.
- **Reintentar** — con clasificación previa de transitorio y permanente.
- **Detectar fallos** — cada envío registra su resultado y su identificador en Meta.
- **Limitar la exposición** — sin webhook no hay punto de entrada público para este canal.

## Escenarios y requisitos relacionados

- `QA-DISP-01` — Meta devolviendo 503 no impide publicar el Kudo.
- `QA-MANT-01` — cambiar de proveedor es un adaptador nuevo.
- `QA-CONF-02` — un reproceso no envía dos WhatsApp.
- `RF-02`, `RF-07`, `RF-15`, `DRV-003`, `RT-07`.

## ADRs relacionados

- **ADR-0005** — los reintentos viven en la bandeja de eventos.
- **ADR-0006** — el gateway que decide cuándo usar este canal.
- **ADR-0004** — este mismo adaptador verifica el celular.
- **ADR-0018** — dónde vive el token.

## Notas de implementación

- El token de usuario del sistema se genera desde el principio: el token por defecto del portal caduca en horas y hace perder tiempo de depuración buscando un error que no existe.
- La lista de destinatarios de prueba se registra en el portal de Meta **antes** de la demostración, incluido el número del docente si va a recibir un Kudo en vivo.
- El adaptador registra el identificador de mensaje que devuelve Meta: es la evidencia de envío que pide la entrega.
- Las pruebas del adaptador real no llaman a Meta: verifican la construcción del cuerpo de la petición y la clasificación de errores con respuestas simuladas. La prueba contra Meta se hace a mano y queda como evidencia en la entrega.

## Referencias

- Meta. *WhatsApp Business Cloud API — Send Messages, Message Templates.* developers.facebook.com.
- Meta. *Conversations and the 24-hour customer service window.*
- ITU-T. *Recommendation E.164 — The international public telecommunication numbering plan.*
