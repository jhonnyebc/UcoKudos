# Guía: enviar la plantilla de WhatsApp a aprobación de Meta

**Es la tarea más urgente del proyecto.** Es el riesgo P-01 del DAS y lo único que no depende del autor: la aprobación la decide Meta y no se puede acelerar trabajando más.

## Por qué hace falta una plantilla

Meta solo permite enviar texto libre dentro de la **ventana de conversación de 24 horas**, que se abre cuando el usuario escribe al negocio.

El destinatario de un Kudo **nunca escribió a la plataforma**. Así que UCO Kudos está **siempre** fuera de esa ventana, y todos sus envíos tienen que ir por una plantilla aprobada de antemano (ADR-0007). Sin plantilla aprobada, el adaptador recibe un error permanente de Meta y **no sale ni un mensaje**.

## Antes de empezar

| Necesita | Dónde |
|---|---|
| Cuenta de desarrollador de Meta | Ya la tienes |
| Una app con el producto WhatsApp añadido | Portal de Meta for Developers |
| El número de prueba que Meta regala | Se crea solo al añadir el producto |

## Paso a paso

### 1. Abrir la configuración de WhatsApp de la app

1. Entra a `developers.facebook.com` y abre **Mis aplicaciones**.
2. Elige la app (o crea una de tipo **Negocio** si no la tienes).
3. En el menú lateral, **WhatsApp → Configuración de la API**.

Aquí verás el **número de teléfono de prueba** que Meta asigna, con su **identificador de número de teléfono**. Ese identificador va en `WHATSAPP_PHONE_NUMBER_ID` del `.env`; no es secreto.

### 2. Registrar los destinatarios de prueba

**Hazlo ahora, no el día de la demostración.** El número de prueba solo envía a una lista corta de destinatarios registrados de antemano, del orden de cinco.

1. En la misma pantalla, busca **A** (el campo de destinatario).
2. **Administrar lista de números de teléfono** → **Agregar número**.
3. Escribe el número en formato internacional, con indicativo: `+57 300 ...`.
4. Llega un código de verificación por WhatsApp a ese número. Introdúcelo.

Registra, como mínimo:

- Tu celular.
- Un segundo número tuyo, para probar autor y destinatario distintos.
- **El celular del docente**, si va a recibir un Kudo en vivo durante la sustentación.

### 3. Crear la plantilla del aviso de Kudo

1. Menú lateral: **WhatsApp → Administrador de WhatsApp**, o entra al **Administrador comercial** y abre **Herramientas de cuenta → Plantillas de mensajes**.
2. **Crear plantilla**.
3. Rellena así:

| Campo | Valor |
|---|---|
| **Categoría** | **Utilidad** |
| **Nombre** | `kudo_recibido` |
| **Idioma** | Español |

El nombre tiene que ser **exactamente** `kudo_recibido`: es el que el catálogo de mensajes usa para buscar la plantilla, en `CatalogoMensajes`. Si lo cambias aquí, hay que cambiarlo también allí.

La categoría importa: **Utilidad** es para avisos sobre algo que ocurrió, que es el caso. Si eliges **Marketing**, la revisión es más estricta y la pueden rechazar.

4. En el **cuerpo** del mensaje, escribe exactamente:

```text
Hola, recibiste un reconocimiento de {{1}} en la categoría {{2}} en tu cuenta de UCO Kudos.
```

Los `{{1}}` y `{{2}}` son las dos variables, **en este orden**: autor y categoría. Es el orden en que el gateway las envía, así que invertirlas aquí haría que los mensajes salieran al revés.

**Dos lecciones del envío real, el 2026-11-09:**

- Meta **rechaza las plantillas que empiezan o terminan con una variable**. Por eso los dos cuerpos empiezan con «Hola,».
- El clasificador de Meta avisó de que la plantilla del Kudo «será rechazada» como Utilidad porque la frase «Entra a UCO Kudos para verlo» le parecía promocional. Se reescribió para que describa un hecho ocurrido en una cuenta existente, que es la definición de Utilidad, y dejó de avisar. La categoría importa: Marketing está sujeta a que el usuario no haya desactivado promociones, y un aviso de reconocimiento no es una promoción.
- La segunda plantilla va en **Utilidad y no en Autenticación** a propósito: la categoría Autenticación impone su propio formato, con botón de copiar código y su manejo de la caducidad, y eso obligaría a cambiar cómo el gateway arma el mensaje.

El «Hola,» del principio no es adorno: **Meta rechaza las plantillas que empiezan o terminan con una variable**. Se comprobó el 2026-11-09, cuando el editor no dejó enviar el cuerpo que empezaba directamente por `{{1}}`. El texto que envía el código no cambia por esto, porque el orden de las variables es el mismo.

5. Meta pedirá **ejemplos** para las variables. Pon algo realista:
   - `{{1}}` → `Ana Gómez`
   - `{{2}}` → `Trabajo en Equipo`

6. **Envía la plantilla.**


### 4. La segunda plantilla: por qué no existe

La idea era una plantilla `codigo_verificacion` para verificar el celular (RF-02). **No se pudo crear, y conviene saber por qué antes de volver a intentarlo.**

Meta clasifica los códigos de verificación en la categoría **Autenticación** y no acepta otra: al intentar enviarla como Utilidad, el propio panel avisa de que «esta plantilla será rechazada». Y las plantillas de categoría Autenticación **no se pueden crear desde una cuenta de WhatsApp Business sin verificación comercial**: el panel responde «esta cuenta de WhatsApp Business no tiene permiso para crear una plantilla de mensaje». Se comprobó el 2026-11-09 con la cuenta de prueba.

La verificación comercial del negocio exige documentos y un plazo que no depende de nosotros, así que no es un camino viable antes de la entrega.

**Qué se hizo en su lugar.** El mensaje del código de verificación sale por **correo** y queda como **aviso dentro de la aplicación**. El cambio son dos valores: el mensaje deja de declarar plantilla de WhatsApp en `CatalogoMensajes`, y su política de canal pasa a correo y tiempo real en `PoliticaCanal`.

Ese detalle es defendible y vale la pena decirlo en la sustentación: **una restricción de un proveedor externo se absorbió cambiando dos valores de configuración, sin tocar el dominio, los casos de uso ni el gateway**. Es exactamente lo que ADR-0006 perseguía al separar el mensaje del canal; si en vez de eso cada caso de uso hubiera llamado a WhatsApp directamente, este cambio habría sido una cacería por todo el código.

Si algún día se verifica el negocio, se crea la plantilla y se vuelve a poner su nombre en el catálogo: el canal se reactiva solo para ese mensaje.


### 5. Generar el token que no caduca

Mientras se aprueba la plantilla, resuelve esto, porque es el otro tropiezo seguro.

El token que el portal muestra en **Configuración de la API** **caduca en unas horas**. Si lo usas, el canal funcionará hoy y mañana fallará, y perderás tiempo buscando un error que no existe.

1. Entra al **Administrador comercial** → **Configuración del negocio**.
2. **Usuarios → Usuarios del sistema** → **Agregar**.
3. Crea un usuario del sistema con rol de **administrador**.
4. **Agregar activos** → elige la cuenta de WhatsApp → permiso de **control total**.
5. **Generar nuevo token** → elige la app → marca los permisos `whatsapp_business_messaging` y `whatsapp_business_management`.
6. En la caducidad elige **Nunca**.
7. **Copia el token y guárdalo donde no se pierda: solo se muestra una vez.**

Ese token va en `WHATSAPP_ACCESS_TOKEN` del `.env`. **Es un secreto**: no entra al repositorio, y el escaneo del gancho antes del envío lo bloquearía si lo intentaras (ADR-0018).

### 6. Esperar la aprobación

Las plantillas quedan en estado **Pendiente**. Meta suele resolver en minutos o en pocas horas, pero no hay garantía, y por eso esto va primero.

Mientras esperas, **el desarrollo no se bloquea**: con `WHATSAPP_PROVEEDOR=Simulado` en el `.env`, la plataforma usa el adaptador simulado y el flujo completo funciona sin tocar la red. Eso es lo que permite que las pruebas corran en el pipeline sin gastar cuota.

### 7. Conectar el canal real

Cuando las dos plantillas estén en **Aprobada**:

1. En el `.env`, pon:

```text
WHATSAPP_PROVEEDOR=MetaCloud
WHATSAPP_PHONE_NUMBER_ID=<el identificador del paso 1>
WHATSAPP_ACCESS_TOKEN=<el token del paso 5>
```

2. **Reinicia la API**: los secretos se leen al arrancar.
3. Publica un Kudo a uno de los números registrados y comprueba que llega.

## Si una plantilla se rechaza

Mira el motivo que da Meta en el Administrador de plantillas. Las causas más comunes:

| Motivo | Qué hacer |
|---|---|
| Categoría equivocada | Cámbiala a **Utilidad** y reenvía |
| Texto que parece promocional | Quita cualquier cosa que suene a invitación o promoción |
| Variables al principio o al final del mensaje | Meta rechaza una plantilla que empieza o acaba con una variable. El cuerpo del paso 3 ya respeta esto |
| Ejemplos poco realistas | Pon nombres y valores que parezcan de verdad |

Corregir y reenviar es rápido, pero vuelve a la cola de revisión: razón de más para no dejarlo para el final.

## Lo que esta guía no cubre

**Número propio verificado.** Es lo correcto en producción y quita el límite de cinco destinatarios, pero exige verificación de empresa de Meta, que tarda más que el plazo de entrega. Queda documentado como la evolución en ADR-0007, y es una de las limitaciones que hay que declarar al sustentar.

## Resumen para hoy

1. Registrar los destinatarios de prueba, el del docente incluido.
2. Enviar `kudo_recibido` a aprobación.
3. El código de verificación **no lleva plantilla**: sale por correo y como aviso en la aplicación (sección 4).
4. Generar el token de usuario del sistema, sin caducidad.

Con eso, el riesgo P-01 queda en marcha y el resto del plan puede avanzar en paralelo con el adaptador simulado.
