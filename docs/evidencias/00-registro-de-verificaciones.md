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

### La tabla no se deja editar: el disparador rechaza el cambio

`10-auditoria-disparador-rechaza.png`: un `UPDATE` sobre `audit.evento_auditoria`, lanzado desde
`psql` con el usuario de la aplicación contra la base del servidor. Respuesta de PostgreSQL:

```
ERROR: La tabla de auditoria es de solo insercion: no se permite UPDATE (ADR-0013).
CONTEXT: PL/pgSQL function audit.rechazar_cambio() line 1 at RAISE
```

**Este error es la evidencia, no un fallo.** La auditoría tiene dos defensas y conviene no
confundirlas:

| Defensa | Qué hace | Dónde vive |
|---|---|---|
| Disparador de solo inserción | **Impide** la manipulación | La base, ADR-0013 |
| Cadena de hashes SHA-256 | La **detecta** | El dominio |

El disparador actúa antes de cualquier actualización o borrado, así que **ni la propia aplicación
puede alterar el registro**: no es una convención que dependa de que nadie escriba el `UPDATE`
equivocado, es una garantía del motor. La cadena cubre el caso que el disparador no puede cubrir,
que es alguien con permisos para desactivarlo.

### La cadena detecta la alteración, y dice dónde

`10-auditoria-cadena-alterada.png`: tras desactivar el disparador y modificar **una fila
intermedia**, la misma operación responde:

> *La cadena se rompe en el evento 4: su hash no corresponde a sus datos, así que el registro fue
> modificado.*

**Esta es la captura que vale, y conviene explicar por qué.** Un «Cadena íntegra» en verde solo
demuestra que la operación se ejecuta: una función que devolviera siempre «todo bien» daría
exactamente la misma pantalla. Lo que prueba que la verificación sirve es verla **fallar cuando
debe**.

Tres detalles que merecen señalarse:

- **Señala el evento concreto**, el 4, no un «algo no cuadra». Eso permite acotar qué se tocó.
- **Distingue el tipo de manipulación.** Dice que el hash no corresponde a *sus datos*, es decir
  una **edición**. Un borrado daría un mensaje distinto, porque lo que falla entonces es el enlace
  con el eslabón anterior.
- **Revisó 4 eventos y no los 16** de la verificación anterior: se detiene en la primera rotura.
  Seguir no aportaría nada, porque a partir de ahí toda la cadena hereda el desajuste.

Se alteró una fila intermedia a propósito. Si se hubiera tocado la última, la detección no probaría
que recorre la cadena: bastaría con comprobar el final.

La fila se restaura después con `sh scripts/demostrar-auditoria.sh restaurar`, que la devuelve a su
valor original y borra el respaldo. La demostración no deja residuos en la base.

## 2026-11-11 — Integración continua

`01-ci-ejecuciones.png`: el historial de ejecuciones del back-end, **46 en total**, todas en verde,
con las dos ramas (`main` y `develop`) y el commit que disparó cada una.

Lo que enseña esta vista y no enseñaría una sola ejecución: que el pipeline **se ejecuta en cada
envío**, no una vez para la captura. Cada commit del día aparece dos veces, una por rama, y todas
terminan en verde en menos de dos minutos.

Conviene acompañarla del detalle de una ejecución, donde se ven los seis trabajos con su nombre:
es lo que permite decir qué comprueba cada puerta.

### Los seis trabajos, y el que solo corre en `main`

`01-ci-trabajos-de-una-ejecucion.png`: el detalle de una ejecución, con los seis trabajos y su
nombre:

1. Backend (build, formato, pruebas y cobertura)
2. Escaneo de secretos (ADR-0018)
3. Escaneo de vulnerabilidades (Trivy)
4. Análisis estático (Sonar)
5. Imagen de contenedor (build y Trivy)
6. Publicar la imagen en el registro

El sexto **solo aparece en las ejecuciones de `main`**, por su condición de rama. Comparar esta
captura con una de `develop`, que tiene cinco, enseña esa condición funcionando sin leer el
workflow.

## 2026-11-11 — Entrega continua

`02-cd-publicacion-con-etiqueta.png`: la publicación real en el registro, con la etiqueta
`0.1.0-116fd0c` y el destino `ghcr.io/jhonnyebc/ucokudos-api`.

El nombre del paso lo dice entero: **«Publicar `<versión>-<sha>` (nunca más reciente)»**. No existe
una etiqueta `latest`, y eso es deliberado: con `latest` nadie sabe qué está corriendo en el
servidor, y dos despliegues del mismo nombre pueden ser cosas distintas. Con `0.1.0-116fd0c` se
sabe exactamente qué commit está en producción.

`02-cd-imagen-escaneada-es-la-publicada.png`: **la captura menos vistosa y una de las más
importantes.** Muestra que el trabajo de publicación **descarga la imagen como artefacto** en lugar
de reconstruirla, y verifica su huella:

```
SHA256 digest of downloaded artifact is dcff1356238f648faeac5ab31fa2a32f149cbb4e539b936196e95ff1a96d6506
```

Lo que garantiza es que **la imagen publicada es exactamente la que Trivy escaneó**. Si el trabajo
de publicación la reconstruyera, sería una imagen distinta —otras capas base, otra fecha, otras
dependencias resueltas— y el escaneo anterior no diría nada sobre ella. Sería una puerta de
seguridad que se salta a sí misma sin que nadie lo note.

### El despliegue no lo dispara el pipeline, y es a propósito

La entrega llega hasta el registro. Poner esa versión en el servidor es una acción separada,
`./scripts/desplegar.sh <etiqueta-api> <etiqueta-web>`, que ejecuta una persona.

Que un `git push` toque producción sin que nadie lo decida es cómodo hasta el día que no lo es.
Las dos etiquetas separadas permiten además desplegar solo lo que cambió: en el despliegue del
cliente de esta noche, el contenedor de la API no se recreó.
