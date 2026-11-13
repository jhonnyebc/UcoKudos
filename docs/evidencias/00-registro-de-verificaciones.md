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

## 2026-11-11 — Análisis estático y escaneo de vulnerabilidades

`03-sonar-puerta-de-calidad.png`: el tablero de SonarCloud sobre `main`, **puerta de calidad
aprobada**, 6,3 mil líneas analizadas, 0 problemas nuevos y **81,25 % de cobertura sobre código
nuevo**, con un mínimo exigido del 80 %.

Esa cifra mide el **núcleo** y no toda la solución: el análisis excluye `UcoKudos.Infrastructure` y
`UcoKudos.Api` de la cobertura, que es exactamente lo que ADR-0017 quería medir. La cobertura
global, contando adaptadores y *endpoints*, es del 18,34 %, y esos se prueban con las de
integración, no con las unitarias.

**Lo que hay que declarar y no esconder:** la puerta se cumple y se ve en el tablero, pero **no
detiene el pipeline**. El análisis se envía sin `sonar.qualitygate.wait`, así que el trabajo
termina antes de que SonarCloud evalúe el resultado. Un incumplimiento futuro no rompería la
construcción.

`04-trivy-bloquea-la-imagen.png`: **la mejor evidencia de las doce, y es una captura en rojo.**

El resumen del escaneo de la imagen `ucokudos-api:0.1.0-8579449` sobre Ubuntu 24.04:

| Destino | Tipo | Vulnerabilidades |
|---|---|---|
| ucokudos-api (ubuntu 24.04) | ubuntu | 0 |
| app/UcoKudos.Api.deps.json | dotnet-core | 0 |
| **Microsoft.AspNetCore.App 10.0.14** | dotnet-core | **3** |
| Microsoft.NETCore.App 10.0.14 | dotnet-core | 0 |

Las tres son `CVE-2026-114146`, `114147` y `114149`, de severidad HIGH, corregidas en 10.0.15.

**Ninguna la introdujo el código.** Aparecieron porque la base de datos de Trivy se actualiza a
diario, y el mismo commit que ayer pasaba hoy no pasa. Ese es justo el riesgo que una puerta
bloqueante cubre y una informativa no: sin ella se habría desplegado una imagen vulnerable sin que
nadie se enterara. Se corrigió subiendo la imagen base a 10.0.15.

Un detalle de la configuración que merece explicarse: Trivy corre con `ignore-unfixed: true`, así
que solo bloquea por lo que **tiene corrección disponible**. Bloquear por algo sin parche detiene
el trabajo sin ofrecer salida, y así es como se acaba desactivando la puerta.

## 2026-11-11 — Publicación en el registro

`05-publicacion-api.png` y `05-publicacion-web.png`: los dos paquetes en GHCR, **públicos**, con su
historial de versiones.

No hay ninguna etiqueta `latest`: cada publicación es `0.1.0-<sha corto>`. El historial permite
saber qué se publicó y cuándo, y es lo que hace posible desplegar una versión concreta y volver
atrás a otra.

Que sean públicos tiene una consecuencia práctica: el servidor **no necesita credenciales** para
descargarlas, así que no hay un secreto de registro que gestionar ni que rotar en la máquina.

## 2026-11-11 — Pruebas: los cuatro niveles

Recuentos tomados del pipeline, no de la memoria:

| Nivel | Cuántas | Qué impide que una prueba del nivel de abajo no impide |
|---|---|---|
| Unitarias | **107** | Que una regla del dominio se incumpla. Corren en 229 ms, así que se ejecutan a cada cambio |
| Arquitectura | **6** | Que el dominio referencie infraestructura. El compilador lo permite; estas no |
| Integración | **57**, 1 omitida | Lo que solo falla contra PostgreSQL real: la traducción a SQL, el formato de las columnas, el encadenado de hashes |
| Componentes del cliente | **52** | Que la interfaz deje de ser usable o accesible |

**Por qué los cuatro niveles y no más de uno solo.** Cada uno coge una clase de fallo distinta, y
este proyecto tiene ejemplos reales de cada una:

- Las **de arquitectura** detectaron una violación real de la regla de dependencias que compilaba
  sin problema.
- Las **de integración** destaparon que el muro devolvía 500 siempre: la consulta proyectaba y
  luego ordenaba sobre el resultado proyectado, y EF no lo traduce. Las unitarias pasaban.
- Las **de componentes** cubren el teclado y la accesibilidad del buscador de personas, que una
  prueba de API no puede ver.

**La prueba omitida está declarada, no escondida.** Comprueba que el almacenamiento rechace las
peticiones anónimas, y el servidor compatible con S3 que se usa en pruebas las atiende, así que
pasaría siempre y no probaría nada. Dejarla activa sería peor que omitirla: daría verde sin
comprobar lo que dice comprobar.

`12-pruebas-pasos-del-pipeline.png`: los pasos del trabajo de back-end. Los tres niveles aparecen
como **pasos separados**, cada uno con su propio tiempo:

- Pruebas unitarias con gate de cobertura del núcleo — 6 s
- Pruebas de arquitectura (reglas de ADR-0001) — 10 s
- Pruebas de integración (Testcontainers) — 36 s

Están separados a propósito y no agrupados en un `dotnet test` único. Cuando uno falla, el nombre
del paso ya dice **qué clase de fallo** es: una regla de negocio rota, una violación de la
arquitectura o algo que solo aparece contra PostgreSQL real. Agruparlos obligaría a leer el
registro para averiguar lo que el nombre puede decir solo.

Se ve además que los 36 segundos de integración son más que todo lo demás junto, lo cual es
esperable: levantan un PostgreSQL de verdad con Testcontainers. Ese coste es la razón de la
pirámide, no un problema.

### Los recuentos, paso a paso

`12-pruebas-unitarias-y-cobertura.png`, `12-pruebas-de-arquitectura.png` y
`12-pruebas-de-integracion.png`: la salida de cada nivel.

```
Passed! - Failed: 0, Passed: 107, Skipped: 0, Total: 107  (unitarias, 229 ms)
Passed! - Failed: 0, Passed:   6, Skipped: 0, Total:   6  (arquitectura, 1 s)
Passed! - Failed: 0, Passed:  57, Skipped: 1, Total:  58  (integración, 15 s)
```

La omitida aparece con nombre y todo:
`AlmacenObjetosRealTests.Pedir_el_objeto_sin_firma_es_rechazado [SKIP]`. Está declarada arriba.

**La captura de las unitarias trae además la tabla de cobertura, y es el dato más honesto de la
entrega:**

| Proyecto | Líneas | Ramas |
|---|---|---|
| `UcoKudos.Domain` | 47,24 % | 44,17 % |
| `UcoKudos.Application` | **0,93 %** | 1,78 % |
| Total | 18,8 % | 25,77 % |

Ese 0,93 % no quiere decir que la capa de aplicación esté sin probar. El informe sale **solo de las
unitarias**, y los casos de uso se ejercitan sobre todo desde las 57 de integración, cuya cobertura
no se recoge ni se fusiona. **El instrumento mide menos de lo que el proyecto prueba.**

Es una limitación metodológica y conviene decirla antes de que la pregunten: fusionar la cobertura
de los tres proyectos de prueba daría un número representativo. Hoy no se hace, así que la cifra
publicada subestima lo que realmente se ejercita.

## 2026-11-11 — Observabilidad: la traza que cruza la frontera asíncrona (QA-OBS-01)

`11-observabilidad-traza-frontera-asincrona.png`: una publicación completa en **una sola traza**,
`6e54f7d`, con **17 tramos**, profundidad 3 y 2,27 s de duración total, contra el sistema
desplegado.

La traza se lee en dos bloques separados por un hueco:

| Momento | Qué ocurre | Duración |
|---|---|---|
| 0 s | `POST /api/kudos/` y sus 8 consultas a PostgreSQL: el Kudo, la auditoría y el evento, en una transacción | **0,31 s** |
| 0,31 s – 1,13 s | *Nada.* El usuario ya tiene su respuesta | — |
| 1,13 s | `notificaciones.procesar`: el despachador recoge el evento de la bandeja | **0,91 s** |
| dentro | `HTTP POST 200 graph.facebook.com:443` — el envío real del WhatsApp | **0,8 s** |

**Ese hueco es la evidencia.** La petición terminó en 0,31 s y los avisos salieron casi un segundo
después, pero las dos cosas comparten identificador de traza. Eso es lo que permite responder «qué
le pasó a esta publicación concreta» siguiendo un solo hilo, que es literalmente el escenario
QA-OBS-01.

**Y la traza aporta un argumento que ningún documento puede dar: un número.** De los 0,91 s que
tarda el despachador, **0,8 s son la llamada a Meta**. Si ese envío ocurriera dentro de la
petición, publicar un Kudo pasaría de 0,31 s a más de un segundo, y además fallaría entero si Meta
no respondiera. La bandeja transaccional de ADR-0005 no es una preferencia de diseño: aquí se ve
medido lo que evita.

Conviene señalar también lo que **no** aparece: ningún tramo de correo, porque en el servidor ese
canal sigue en modo simulado y el simulado no sale a la red. La traza no solo muestra lo que pasa;
también delata lo que falta.

## 2026-11-12 — Correo real, y los tres canales a la vez

`07-correo-recibido.png`: el correo recibido en Gmail, **con su HTML pintado**: el título, el
cuerpo con el autor y la categoría, y el pie institucional.

Esa captura cierra un defecto encontrado días antes: el HTML viajaba etiquetado como `text/plain`
porque se mezclaba `Body` con vistas alternativas, y **ningún cliente lo pintaba**. Se corrigió con
dos vistas alternativas explícitas, la enriquecida al final. Hasta ahora solo se había comprobado
contra un contenedor de captura; esta es la primera verificación contra un cliente de correo de
verdad.

### La publicación que disparó los tres canales reales

Es la primera vez que los tres salen de verdad en la misma publicación:

| Canal | Estado | Identificador externo |
|---|---|---|
| Correo | Enviado | `smtp:aceptado` — ya no `simulado:N` |
| WhatsApp | Enviado | `wamid.HBgMNTcz…` — identificador de Meta |
| TiempoReal | Enviado | `persistido` |

El evento se encoló a las 21:40:32 y se procesó a las 21:40:35: **tres segundos** para los tres
canales, con `intentos = 0`.

El historial de la tabla cuenta la transición sola. Las filas de `Correo` anteriores dicen
`simulado:1`, `simulado:2`, `simulado:3`; la última dice `smtp:aceptado`. **Mismo código, mismo
puerto, otro adaptador detrás**, y el caso de uso que publica el Kudo no se enteró de nada. Ese
contraste dentro de una sola tabla es el argumento de la arquitectura hexagonal mejor de lo que
puede decirlo un diagrama.

### El proveedor, dicho con precisión

El envío sale por **SMTP de Gmail** con una contraseña de aplicación, no por un proveedor
transaccional. Se intentó con Brevo y su verificación por SMS no llegó.

La diferencia hay que declararla: Gmail no da métricas de entrega, ni gestión de rebotes, ni
reputación de dominio propio, y tiene un límite diario bajo. Para una plataforma en producción lo
correcto es un proveedor transaccional. Para demostrar que el adaptador real funciona y que el
mensaje llega con su formato, es equivalente: el camino que recorre el correo es el mismo.

## 2026-11-12 — Despliegue, visto desde un celular con datos móviles

`06-despliegue-desde-datos-moviles.jpg`: la plataforma abierta en un iPhone, con sesión iniciada
como Rector y el muro cargado con sus tres Kudos.

**El dato que hace válida esta captura está en la barra de estado: `LTE`, no wifi.** Es el criterio
de cierre de la Fase 4 del plan, y el más importante de todo: *«el docente abre la URL pública
desde un celular con datos móviles, fuera de la red de casa»*. Una captura hecha desde el wifi de
casa no demuestra que el servicio sea alcanzable desde internet.

Lo que se ve además, y no es decorativo:

- **La interfaz es usable con una mano** y sin desplazamiento horizontal, que es QA-MOV-01. Las
  tarjetas ocupan el ancho, el texto no se corta y la navegación está abajo, al alcance del pulgar.
- **Las tildes se ven correctas** —«Benítez», «Empatía», «Espíritu UCO»— en un tercer cliente
  distinto: después del navegador de escritorio y de WhatsApp, también en Safari móvil.
- **La pestaña de Auditoría aparece**, así que la verificación de la cadena se puede ejecutar desde
  un celular igual que desde el escritorio.
- El dominio es `…sslip.io`, que **no se compró**: resuelve la IP que lleva en el propio nombre, y
  el proxy pidió y renueva el certificado solo. Sin HTTPS no habría service worker ni
  notificaciones, así que no era una mejora opcional.

## 2026-11-12 — Notificaciones push, y los canales coincidiendo en la pantalla bloqueada

`09-push-pantalla-bloqueada.jpg`: un iPhone **bloqueado**, con datos móviles, y **dos
notificaciones del mismo Kudo llegando a la vez**:

| Aviso | Canal | De dónde sale |
|---|---|---|
| «Hola, recibiste un reconocimiento de Marta Restrepo…» | WhatsApp | Plantilla `kudo_recibido` aprobada por Meta |
| «Recibiste un Kudo — Marta Restrepo te reconoció…» | **Web Push** | La PWA instalada, con VAPID |

**La de abajo es la que cierra la evidencia 9.** Lleva el icono de la aplicación instalada, no el
de un navegador: es Web Push entregado por el service worker con la aplicación **cerrada** y el
teléfono bloqueado, sobre HTTPS real. Sin certificado de una autoridad real no habría service
worker ni notificaciones, así que esta captura también justifica por qué el HTTPS no era opcional.

En iOS hay un requisito que conviene saber explicar: **el push solo funciona si la PWA está
instalada en la pantalla de inicio**. Desde el navegador, aunque sea el mismo motor, Apple no lo
permite. Y el permiso concedido en el navegador no se hereda a la aplicación instalada: para iOS
son dos cosas distintas.

**Que los dos avisos coincidan demuestra el reparto del Gateway**, que es ADR-0006: una sola
publicación, varios canales independientes, cada uno con su propio adaptador y su propio
resultado. Si uno fallara, los demás llegarían igual.

Conviene señalar también lo que **no** se ve: ningún aviso en vivo por SSE, y es correcto. El
Gateway entrega por el canal en vivo solo si hay una conexión abierta; como la aplicación estaba
cerrada, eligió Web Push. No notifica dos veces lo mismo (ADR-0009).

El «from Kudos» del título lo añade iOS con el nombre de la aplicación instalada: no sale del
catálogo de mensajes.

## 2026-11-12 — Respaldo con restauración probada

Ejecutado contra el servidor. El respaldo produce dos archivos: el volcado de la base (52 KB) y
el volumen de objetos (6,3 KB).

La restauración se comprueba **sobre una base aparte**, `ucokudos_restauracion`, y compara los
recuentos con la base viva:

| Tabla | Original | Restaurada |
|---|---|---|
| `nucleo.persona` | 6 | 6 |
| `nucleo.kudo` | 9 | 9 |
| `audit.evento_auditoria` | 36 | 36 |
| `notif.notificacion` | 27 | 27 |
| `notif.outbox_mensaje` | 9 | 9 |

Resultado: **RESTAURACIÓN CORRECTA**.

**Esto cierra la mitigación del riesgo P-04**, que hasta hoy estaba declarada en el DAS y no
existía: la fila prometía una protección inexistente, que es peor que un riesgo asumido
abiertamente.

**Por qué se restaura en una base aparte y no encima.** Un respaldo sin restauración probada no es
un respaldo: es un archivo del que nadie sabe si sirve. Y la forma habitual de averiguarlo
—restaurar sobre lo bueno— solo se puede hacer el día de la catástrofe, cuando ya no queda margen
para descubrir que el archivo estaba mal. Restaurar al lado permite comprobarlo cualquier día, con
la plataforma funcionando.

El volumen de objetos ocupa 6,3 KB porque **no hay adjuntos subidos**: la pantalla de adjuntos es
una de las ausencias declaradas. El respaldo los cubriría igual.

El riesgo residual **sigue siendo alto** y conviene no disfrazarlo: esto es recuperación, no
disponibilidad. Si la máquina cae, la plataforma cae; lo que cambia es que ahora los datos se
pueden recuperar en otra.

## 2026-11-12 — Adjuntos: el primer uso real encontró un defecto que ninguna prueba veía

Al subir un adjunto desde un celular contra producción, el Kudo se publicó pero el adjunto falló
con *«Otra operación modificó este contenido al mismo tiempo. Vuelva a intentarlo»*, un 409 de
concurrencia, sin que nadie estuviera tocando nada.

**Causa.** El identificador del adjunto lo asigna el dominio, pero EF Core asume por omisión que una
clave `Guid` la genera él al insertar. Cuando una entidad nueva aparece dentro de la colección de un
agregado ya cargado y su clave ya viene rellena —lo que hace `Kudo.AgregarAdjunto`—, EF concluye que
la fila existe y emite un `UPDATE` en lugar de un `INSERT`. Cero filas afectadas, y lo traduce como
conflicto de concurrencia.

**Por qué ninguna prueba lo vio.** Ninguna añadía un adjunto a un Kudo cargado de la base, que es el
camino real. Las de integración cubrían el almacenamiento y los casos de uso por separado, con datos
recién creados.

**Cómo se trabajó, que es lo que conviene contar.**

1. Se escribió primero el escenario de punta a punta que recorre el flujo completo. Se ejecutó
   **antes de arreglar nada** y falló con el mismo 409 y el mismo texto que vio el usuario, en las
   cuatro pruebas que llegan al primer paso.
2. Se corrigió el mapeo (`ValueGeneratedNever`). Es un cambio del modelo y no de las tablas, pero EF
   se niega a aplicar migraciones si el modelo difiere de la instantánea, así que se añadió una
   migración **vacía a propósito**: `Up` y `Down` no hacen nada y la instantánea pierde una línea.
3. Nueve escenarios quedan como regresión: pedir permiso, subir, confirmar, ver el detalle con el
   tipo real detectado, descargar, la auditoría de carga y descarga, el máximo de cinco, solo el
   autor, el contenido que no es del tipo declarado, y el adjunto sin confirmar.
4. Desplegado el 2026-11-12: la migración se aplicó limpia («Esquema actualizado»).

**La pantalla hizo su trabajo.** El Kudo sí se publicó y la pantalla lo dijo con claridad —«el Kudo
se publicó y la persona ya fue avisada, pero un adjunto no se pudo subir»— en lugar de volver al
formulario, donde reenviar lo habría publicado dos veces.

### Segundo fallo: 403 al subir, y rediseño (2026-11-13)

Corregido el 409, la subida dio ahora «el almacenamiento respondió 403». Se reprodujo en local con la
misma imagen de LocalStack: el servidor valida las URL prefirmadas con el secreto fijo `test`, y la
aplicación firmaba con el secreto del despliegue. Las pruebas de integración pasaban porque su
servidor de pruebas usaba también `test`.

**Solución:** los archivos pasan por la API en las dos direcciones, con la sesión; se eliminó del
proxy la ruta pública `/adjuntos`, de modo que el almacenamiento solo es alcanzable por la red
interna. La tabla de verificaciones de la ruta pública (GET anónimo, listado, firma falsa, límite de
7 MiB) **ya no aplica**: esa superficie no existe. Las pruebas de punta a punta ahora recorren el
flujo por HTTP (reservar, subir, confirmar, descargar) y comprueban autor, tipo real, tamaño,
anónimo, identidad sin cuenta y auditoría. Ver ADR-0012, revisión del 2026-11-13.

**Tercer fallo, ya con el rediseño desplegado:** la subida dio un 500 y el registro del servidor dijo
`The specified bucket does not exist`. El adaptador afirmaba crear el contenedor «al arrancar», pero
nada lo llamaba: con URL prefirmadas la subida nunca llegó a ejecutarse, así que no se había notado.
Las pruebas tampoco lo veían, porque su fixture llamaba a `AsegurarContenedorPrivadoAsync` por el
código. Corregido: `GuardarAsync` crea el contenedor (privado, idempotente) si el almacenamiento
responde `NoSuchBucket`, y hay una prueba que guarda contra un contenedor que no existe.

**Verificado en producción (2026-11-13, desde el celular):** se publicó un Kudo con una imagen, el adjunto se subió y se abrió desde el muro. Desplegado con api `0.1.0-702546c` y web `0.1.0-6cac0fb`.

## 2026-11-13 — Registro, verificación y aprobación de cuentas, verificado en producción

Recorrido real contra el sistema desplegado (api `0.1.0-bf44ef3`, web `0.1.0-1ca764b`):

1. Desde `/registro`, sin sesión, se solicitó una cuenta de **Estudiante** en el programa de la
   demostración. Quedó pendiente y no podía entrar.
2. Llegaron por correo los dos códigos (correo y celular) y se verificaron en `/verificar`.
3. El **Rector** abrió «Cuentas» y la lista salió vacía: es lo correcto, porque el Rector solo
   aprueba solicitudes de Coordinador Académico. La de un Estudiante la aprueba el Coordinador de
   Programa de su programa (RF-03).
4. Con una Coordinadora de Programa del mismo programa, la solicitud apareció con «correo
   verificado · celular verificado» y se aprobó.
5. La cuenta quedó **Activa** y la persona entró a la plataforma con su identidad y llegó a la
   pantalla de publicar.

**Hallazgo del camino:** la prueba de punta a punta del flujo encontró, antes de desplegar, que
aprobar al segundo estudiante de un programa fallaba con «ya hay alguien ejerciendo el rol»: la
comprobación de unicidad por programa se aplicaba también a Docente y Estudiante. Corregido.

**Límite que se declara:** el código del celular llega por correo, no por WhatsApp (ADR-0006).

## 2026-11-13 — Reportar y vetar, verificado en producción

Contra el sistema desplegado (api `0.1.0-7e7d43e`, web `0.1.0-96c30e1`): un usuario reportó un Kudo desde
su tarjeta; el Rector lo vio en «Moderación» con el contenido reportado y lo resolvió con **Vetar** y
con **Descartar**. Lo vetado dejó de aparecer en el muro.

**Hallazgos del camino, todos antes de desplegar:** (1) el recuento del veto decía «1 contenido
afectado» al vetar un Kudo con hilo, porque contaba las réplicas después de marcarlas; (2) el cliente
leía como JSON la respuesta vacía (202) de reportar y habría dicho «no se pudo enviar» de algo que sí
se envió; (3) vetar no resolvía los reportes pendientes. Los tres los encontraron las pruebas.

**Calidad:** la puerta de Sonar falló dos veces (19,9 % y 72,4 % de cobertura sobre código nuevo)
porque Sonar solo recibe las pruebas unitarias; se resolvió escribiendo pruebas unitarias de los
casos de uso (87,3 %), sin tocar el umbral.
