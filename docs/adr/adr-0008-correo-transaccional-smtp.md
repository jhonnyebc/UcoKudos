# ADR-0008: correo transaccional por SMTP detrás de un puerto, con proveedor sustituible

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0008 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB del canal correo del ABB de Notificaciones |

## Contexto

RF-14 exige que el destinatario de un Kudo reciba un correo electrónico, con evidencia en la entrega. El correo se usa también para avisar de solicitudes aprobadas o rechazadas (RF-03) y de contenido vetado (RF-12).

El presupuesto es cero (RT-07) y no hay un dominio propio registrado para la plataforma. Las capas gratuitas de los proveedores de correo transaccional imponen límites diarios, y enviar desde un dominio no verificado va a la carpeta de no deseados o es rechazado.

Durante el desarrollo y en el pipeline no se debe consumir cuota ni enviar correos reales a direcciones de prueba.

## Decisión

Enviar el correo por **SMTP a través de un proveedor de correo transaccional en capa gratuita**, detrás de un **puerto de salida propio**.

1. **El puerto es SMTP, no la API del proveedor.** SMTP es un protocolo estándar: cambiar de proveedor es cambiar tres variables de entorno, sin tocar el código. Atarse a la API propietaria de un proveedor daría un poco más de funcionalidad a cambio de un acoplamiento que QA-MANT-01 rechaza.
2. **Proveedor elegido para la línea base:** uno cuya capa gratuita permita del orden de varios cientos de correos diarios, suficiente para desarrollo, pruebas manuales y demostración. La elección concreta se registra en el manual de puesta a punto, porque es un dato de configuración y no una decisión de arquitectura: el ADR fija el mecanismo, no el nombre comercial.
3. **Tres entornos, tres adaptadores del mismo puerto:**
   - **Pruebas automatizadas:** adaptador simulado en memoria. No toca la red.
   - **Desarrollo local:** un servidor SMTP de captura en un contenedor, que atrapa todo el correo y lo muestra en una interfaz web. Nada sale a internet y se puede inspeccionar el HTML real.
   - **Producción:** el proveedor por SMTP con TLS.
4. **Cuerpo en HTML con alternativa en texto plano.** El HTML se arma a partir del catálogo de mensajes de ADR-0006, con estilos en línea y un ancho adaptado a pantalla de celular, porque el 90 % de los destinatarios lo abrirá en el teléfono (DRV-001).
5. **El envío es asíncrono**, a través de la bandeja de eventos de ADR-0005: publicar un Kudo no espera al servidor de correo.
6. **Clasificación de errores** igual que en ADR-0007: un rechazo temporal del servidor es transitorio y se reintenta; una dirección inexistente es permanente, se audita y no se reintenta.
7. **Las credenciales SMTP son un secreto** y viven donde manda ADR-0018.

## Alternativas consideradas

| Criterio | **SMTP con proveedor transaccional (elegida)** | A. API propietaria del proveedor | B. SMTP de Gmail con contraseña de aplicación | C. Servidor de correo propio en la máquina |
|---|---|---|---|---|
| Cambiar de proveedor (QA-MANT-01) | Cumple — tres variables de entorno | **No cumple** — código acoplado al proveedor | Parcial | Parcial |
| Costo (RT-07) | Cumple — capa gratuita | Cumple | Cumple | Cumple |
| Entregabilidad | Cumple — el proveedor gestiona reputación y SPF/DKIM | Cumple | Parcial — límites bajos y riesgo de bloqueo de la cuenta personal | **No cumple** — una IP nueva va directo a no deseados |
| Esfuerzo (RN-02) | Cumple | Parcial | Cumple | **No cumple** — configurar un servidor de correo lleva días |
| Métricas de entrega | Parcial — las del panel del proveedor | Cumple — eventos por API | No cumple | No cumple |
| No usar credenciales personales (DRV-005) | Cumple | Cumple | **No cumple** | Cumple |

- **A. API propietaria** — descartada: daría seguimiento de apertura y eventos de entrega por API, pero ningún requisito los pide y el acoplamiento no se justifica. Si en el futuro hiciera falta, es un adaptador nuevo del mismo puerto.
- **B. SMTP de Gmail con contraseña de aplicación** — descartada: usa la credencial personal del autor para un servicio, lo que es una mala práctica de seguridad, y sus límites son bajos.
- **C. Servidor de correo propio** — descartada: una dirección IP nueva sin reputación, sin SPF ni DKIM, entrega a la carpeta de no deseados. Montarlo y afinarlo no cabe en el plazo y pondría en riesgo la evidencia de RF-14.

## Consecuencias

**Positivas**

- El proveedor es sustituible con un cambio de configuración, sin desplegar código nuevo.
- El correo se prueba de tres formas: simulado en el pipeline, capturado en local con el HTML inspeccionable y real en producción.
- La entregabilidad la gestiona el proveedor, que es quien tiene la reputación de envío.
- Publicar un Kudo no espera al servidor de correo.

**Negativas**

- **Límite diario en la capa gratuita.** Para la línea base es suficiente, pero no soportaría un envío masivo a toda la universidad. Es una limitación conocida que hay que declarar y medir: se instrumenta un contador de correos enviados por día.
- Sin dominio propio verificado, el remitente será una dirección del proveedor y algunos clientes de correo mostrarán un aviso de que fue enviado en nombre de otro. Verificar un dominio es la evolución.
- SMTP no devuelve eventos de entrega: se sabe si el servidor aceptó el correo, no si el usuario lo abrió. Ningún requisito lo exige.

## Tácticas de Bass aplicadas

- **Reducir acoplamiento** — el proveedor detrás de un puerto, y el puerto sobre un protocolo estándar.
- **Encolar** — el envío pasa por la bandeja de eventos.
- **Reintentar** — con clasificación previa del error.
- **Detectar fallos** — cada envío registra su resultado.

## Escenarios y requisitos relacionados

- `QA-MANT-01` — cambiar de proveedor de correo sin tocar el dominio. Este es el escenario que el ADR responde de forma más literal.
- `QA-DISP-01` — el correo caído no impide publicar.
- `RF-03`, `RF-07`, `RF-12`, `RF-14`, `DRV-003`, `DRV-001`.

## ADRs relacionados

- **ADR-0005** — la bandeja que hace asíncrono el envío.
- **ADR-0006** — el gateway que decide cuándo enviar correo y de dónde salen los textos.
- **ADR-0018** — dónde viven las credenciales SMTP.

## Notas de implementación

- El servidor SMTP de captura en local es la forma de ver el correo tal como lo verá el destinatario, incluido el HTML, sin enviar nada a una dirección real ni gastar cuota.
- El correo se revisa en una pantalla de celular antes de la entrega: un correo que se ve bien en el monitor y mal en el teléfono incumple DRV-001 en el 90 % de los casos.
- Se mide la cantidad de correos enviados por día para detectar cuándo se acerca el límite de la capa gratuita antes de que empiece a rechazar.
- El adaptador nunca registra el cuerpo del correo en los logs: puede contener el texto del Kudo, que es información de personas.

## Referencias

- Klensin, J. *Simple Mail Transfer Protocol*, RFC 5321.
- OWASP. *Secrets Management Cheat Sheet.*
- Gamma, E. *et al.* *Design Patterns* — Adapter.
