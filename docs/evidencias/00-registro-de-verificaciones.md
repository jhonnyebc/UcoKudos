# Registro de verificaciones

Lo que se ha comprobado **contra el sistema desplegado**, con la fecha y el dato que lo prueba.

Este archivo existe porque las capturas se pierden y los registros rotan. Lo que queda aquí es el
rastro de qué se verificó, cuándo y con qué prueba, de modo que una afirmación de la sustentación
pueda respaldarse aunque la pantalla ya no exista.

**Sobre los identificadores.** Van truncados a propósito. El `wamid` de WhatsApp lleva el número
de teléfono del destinatario codificado en base64, y este repositorio es público: escribirlo entero
sería publicar un dato personal sin ninguna necesidad. El prefijo basta para reconocerlo y para
distinguirlo de un envío simulado.

## 2026-11-11 — WhatsApp real de punta a punta

Publicación de un Kudo desde `https://kudos.20.83.117.236.sslip.io`, con la plantilla
`kudo_recibido` aprobada por Meta. Mensaje recibido en el celular del destinatario.

| Canal | Estado | Identificador externo |
|---|---|---|
| WhatsApp | Enviado | `wamid.HBgMNTcz…` — identificador real de Meta |
| TiempoReal | Enviado | `persistido` |
| Correo | Enviado | `simulado:3` — proveedor aún simulado |

El evento se encoló a las 17:04:25 y el despachador lo procesó a las 17:04:28, en **menos de tres
segundos**, con `intentos = 0`: salió al primer envío.

**Lo que esta tabla demuestra**, más allá de que el mensaje llegó:

- El Outbox transaccional funciona: el evento se guardó con la publicación y se despachó después.
- El Gateway reparte el mismo aviso por tres canales según la configuración, no según el código
  del caso de uso, que no sabe nada de WhatsApp.
- El adaptador real sustituyó al simulado sin tocar nada más. La fila de las 05:35 del mismo día
  dice `wamid.simulado.1` para el mismo canal y el mismo mensaje: es el mismo puerto con otro
  adaptador detrás, que es el argumento entero de la arquitectura hexagonal.

### El fallo que precedió a este éxito

Dos intentos anteriores, a las 16:22 y 16:55, quedaron en estado `Permanente` con este motivo:

```
HTTP 400: 10 (#10) Viewer cannot access the provided Paid Message Account 1056491520751941
```

Causa: Meta trata la *cuenta de WhatsApp* y la *cuenta de mensajes* como activos distintos, con
permisos independientes. El usuario de sistema tenía control total sobre la primera y ninguno sobre
la segunda, que es la que factura los envíos. Se resolvió asignándole también ese activo; no hizo
falta token nuevo ni desplegar.

**Por qué merece contarse.** El diagnóstico salió de la tabla `notif.notificacion`, no del
registro de la aplicación: el despachador consulta el Outbox cada pocos segundos, así que las
líneas útiles se pierden de vista en minutos, mientras que la columna `error` guarda el motivo
exacto indefinidamente. Se localizó un problema de permisos de un tercero **sin tocar una línea de
código**.

Además el sistema lo clasificó como `Permanente` y no como reintentable, que es la decisión
correcta: un fallo de autorización no se arregla reintentando. Por eso `intentos` siguió en 0 en
lugar de gastar la cola.

### Cómo reproducirlo

```
sh scripts/ver-envios.sh
```

Para volver a probar hay que publicar un Kudo **nuevo**. El índice único sobre evento, destinatario
y canal —la garantía de idempotencia— impide que el mismo evento genere un segundo envío, así que
reprocesar el anterior no manda nada.

## 2026-11-11 — Dos defectos corregidos con prueba de regresión

**El cliente ocultaba el motivo que daba la API.** Publicar un Kudo para uno mismo devolvía 400 con
«No se puede publicar un Kudo para uno mismo» en el cuerpo, y la pantalla decía «Hay algo mal en los
datos enviados. Revisa el formulario», mandando a revisar unos campos que estaban bien. El cliente
nunca leía el cuerpo de una respuesta fallida. Corregido con cinco pruebas, una de ellas para que
el detalle de un 5xx —una traza de Npgsql con nombres de tablas— nunca llegue a la pantalla.

**El gancho de pre-push anunciaba secretos inexistentes.** Con Docker apagado, `docker run`
termina con código 1, el mismo que usa gitleaks para «encontré un secreto». Ahora comprueba que el
demonio responde antes de escanear. Sigue bloqueando el envío, porque enviar sin escanear deja el
repositorio sin esa verificación, pero ya no manda a buscar lo que no existe.

## 2026-11-11 — Autorización de la auditoría, verificada con dos roles

`10-auditoria-rol-sin-permiso.png`: la pantalla de auditoría ejecutada por un **Docente** contra el
sistema desplegado. Respuesta: *«El rol Docente no consulta la auditoría.»*

Consultar la auditoría está restringido a los roles de ámbito universidad —Administrador, Rector y
Coordinador Académico—, que son los que rinden cuentas de la institución. La regla vive en el caso
de uso, no en la pantalla.

**Lo que esta captura demuestra, y que no es solo «salió un error»:**

- La autorización se aplica en el **servidor**. El cliente no sabe qué roles pueden consultar la
  auditoría, y no tiene por qué saberlo: pide, y el servidor decide.
- El motivo llega **íntegro** al usuario. El mensaje es el que escribió el dominio, con el nombre
  del rol dentro, no un texto genérico inventado por la interfaz.

Ese segundo punto es consecuencia directa de una corrección del mismo día: hasta entonces el
cliente descartaba el cuerpo de las respuestas fallidas, y esta misma pantalla habría dicho «no se
pudo completar la operación», que habría parecido un fallo en lugar de una regla funcionando.

### Por qué esta operación tiene pantalla desde hoy

La API ofrecía la verificación desde el principio y estaba cubierta por pruebas, pero **en
producción era inalcanzable**: no figuraba en ninguna interfaz y llamarla a mano exige un token que
el cliente guarda solo en memoria. Una garantía que nadie puede comprobar no tranquiliza a nadie.

### La misma pantalla, con un rol que sí puede

`10-auditoria-cadena-integra.png`: la misma operación ejecutada por el **Rector** contra el mismo
sistema, segundos después. Resultado: *«La cadena está intacta: se verificaron 16 eventos.»*

Las dos capturas juntas valen más que cada una por separado. No cambió el código, ni la pantalla,
ni el servidor: cambió **quién pregunta**. Eso es una regla de autorización demostrada en ejecución,
y responde a RF-03 y RF-04 sin recurrir a un diagrama de permisos.

Los 16 eventos son más que los Kudos publicados porque se audita **toda** acción con consecuencias,
no solo las publicaciones.

**Lo que esta verificación comprueba de verdad.** Cada evento guarda un hash SHA-256 de sus propios
datos encadenado con el del anterior. Recalcularlos detecta las dos formas de manipular un
registro: **editar** una fila, porque su hash deja de corresponder a sus datos, y **borrarla**,
porque el siguiente deja de apuntar a un eslabón existente. Un registro sin encadenar solo detecta
lo primero, y el segundo caso es el que más interesa a quien quiere tapar algo.
