# Manual de puesta a punto — UCO Kudos

Cómo levantar la plataforma desde cero, en local y en el servidor.

**Estado:** cubre lo que existe hoy en el repositorio. Las secciones marcadas **(pendiente)** se completan a medida que se implementan los adaptadores.

## 1. Requisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| .NET SDK | 10.0 o superior | Compilar y ejecutar la API |
| Docker Desktop | Con WSL 2 en Windows | PostgreSQL, objetos, correo y observabilidad |
| Git | Cualquiera reciente | Clonar y los ganchos locales |

Comprobación:

```bash
dotnet --version && docker --version && git --version
```

## 2. Clonar y configurar

```bash
git clone <url-del-repositorio> UcoKudos
```

```bash
cd UcoKudos && cp .env.example .env
```

Abrir `.env` y rellenar. Las variables marcadas **SECRETO** no tienen valor por omisión: si faltan, la aplicación **no arranca**. Es deliberado (ADR-0018): un valor por omisión para un secreto es una contraseña en el repositorio con otro nombre.

Para arrancar en local basta con:

| Variable | Qué poner |
|---|---|
| `POSTGRES_PASSWORD` | Una contraseña cualquiera; se usa solo en el contenedor local |
| `OBJETOS_PASSWORD` | Igual, mínimo 8 caracteres |

El resto puede quedarse con los valores de ejemplo mientras el proveedor correspondiente no esté conectado. Con `WHATSAPP_PROVEEDOR=Simulado` no hace falta token de Meta y nada sale a la red.

## 3. Levantar las dependencias

```bash
docker compose up -d
```

Comprobar que los cuatro contenedores están arriba:

```bash
docker compose ps
```

| Servicio | Dirección en el equipo | Para qué |
|---|---|---|
| PostgreSQL | `localhost:5435` | Base de datos |
| Almacenamiento de objetos | `localhost:9000`, consola en `localhost:9001` | Adjuntos |
| Correo capturado | `localhost:8026` | Ver el correo sin enviarlo a nadie |
| Observabilidad | `localhost:18890` | Trazas, métricas y logs |

Los puertos están desplazados a propósito para poder tener varios entornos locales encendidos a la vez.

## 4. Arrancar la API

```bash
dotnet run --project src/UcoKudos.Api
```

```bash
curl http://localhost:8082/salud/vivo
```

Debe responder `Healthy`.

## 5. Base de datos (pendiente)

Las migraciones se aplican con la herramienta de Entity Framework en cuanto exista la primera. Se documentará el comando aquí.

## 6. Ejecutar las pruebas

Las pruebas se ejecutan **dentro de un contenedor Linux**, no directamente en Windows:

```powershell
./scripts/pruebas.ps1
```

```powershell
./scripts/pruebas.ps1 -Proyecto arquitectura
```

```powershell
./scripts/pruebas.ps1 -Proyecto integracion
```

Sin argumentos corre las unitarias y las de arquitectura. Las de integración se piden aparte porque levantan sus propios contenedores y tardan bastante más. **Docker Desktop debe estar abierto**; el script lo comprueba y avisa si no responde.

### Por qué en un contenedor

En el equipo de desarrollo, **Smart App Control** está activo en modo bloqueo y rechaza cargar ensamblados recién compilados que todavía no tienen reputación. `dotnet build` y `dotnet format` funcionan sin problema, pero el ejecutor de pruebas muere antes de la primera prueba con `An Application Control policy has blocked this file (0x800711C7)`.

El contenedor lo evita sin desactivar ninguna protección del sistema, que además sería irreversible: Smart App Control no se puede volver a activar sin reinstalar Windows.

Hay un segundo beneficio: es el mismo entorno Linux en el que el pipeline ejecuta las pruebas. De hecho el CI invoca **el mismo script**, `scripts/pruebas-en-contenedor.sh`, así que un resultado local y uno del pipeline son comparables y el umbral de cobertura está definido en un único lugar.

La compilación del contenedor usa una carpeta de artefactos propia, separada de `bin/` y `obj/` de Windows: mezclarlas rompe las dos, porque los archivos intermedios guardan rutas absolutas del sistema donde se generaron. El caché de NuGet vive en un volumen con nombre, así que solo la primera ejecución descarga paquetes.

En otro equipo sin Smart App Control, `dotnet test` directo funciona igual; el script sigue siendo la forma recomendada por la paridad con el CI.

## 7. Conectar los proveedores externos

Cada uno funciona en modo simulado hasta que se le dan sus credenciales. El orden recomendado es el de abajo, porque la plantilla de WhatsApp depende de una aprobación de un tercero.

### 7.1 WhatsApp, Meta Cloud API (ADR-0007)

1. En el portal de desarrolladores de Meta, crear la aplicación y añadir el producto de WhatsApp.
2. Anotar el identificador del número de prueba en `WHATSAPP_PHONE_NUMBER_ID`.
3. **Registrar los números de destino.** El número de prueba solo envía a una lista corta de destinatarios registrados de antemano. Incluir aquí los números que se van a usar en la demostración.
4. **Enviar las plantillas a aprobación**, y hacerlo cuanto antes: la aprobación no es inmediata y el destinatario de un Kudo nunca escribe primero, así que **todos** los envíos van por plantilla.
5. Generar un **token de usuario del sistema** y ponerlo en `WHATSAPP_ACCESS_TOKEN`. El token que el portal muestra por defecto caduca en horas y hace perder tiempo depurando un error que no existe.
6. Cambiar `WHATSAPP_PROVEEDOR` a `MetaCloud` y reiniciar la API: los secretos se leen al arrancar.

No hace falta webhook ni túnel: en esta plataforma WhatsApp es solo de salida.

### 7.2 Correo (ADR-0008)

En local no hay que configurar nada: el correo queda atrapado en el contenedor de captura y se lee en `localhost:8026`, con el HTML tal como lo verá el destinatario.

Para enviar de verdad, poner el host, el puerto, el usuario y `SMTP_PASSWORD` del proveedor transaccional. Al ser SMTP, cambiar de proveedor es cambiar esas variables.

### 7.3 Web Push (ADR-0009)

Generar un par de claves VAPID y poner la pública en `VAPID_CLAVE_PUBLICA` y la privada en `VAPID_CLAVE_PRIVADA`.

La pública la necesita el cliente web y **no** es un secreto. La privada **sí**: con ella cualquiera podría enviar notificaciones a los usuarios de la plataforma.

### 7.4 Identidad (ADR-0004)

Crear la aplicación en el proveedor de identidad y poner el emisor en `IDENTIDAD_EMISOR` y la audiencia en `IDENTIDAD_AUDIENCIA`. Ninguno de los dos es secreto.

### 7.5 Almacenamiento de objetos (ADR-0012)

El contenedor de adjuntos debe quedar **privado**: sin lectura anónima. La API firma URL de vida corta después de comprobar permisos. Un contenedor público haría irrelevante toda la estrategia de seguridad de los adjuntos.

## 8. Despliegue en el servidor (pendiente)

El pipeline de despliegue publica la imagen y la reemplaza en el servidor (ADR-0016). Queda por documentar: creación de la máquina, instalación de Docker, proxy inverso con HTTPS, archivo de entorno con permisos restringidos y procedimiento de respaldo.

Conviene dejar escrito también el **procedimiento manual** de despliegue: si el pipeline falla la mañana de la entrega, hay que poder desplegar a mano.

## 9. Problemas conocidos

| Síntoma | Causa y solución |
|---|---|
| La aplicación no arranca y se queja de una variable | Falta un secreto en `.env`. Es el comportamiento correcto: el mensaje nombra la variable |
| Las pruebas de integración fallan al conectar | Docker Desktop cerrado. Abrirlo y esperar a que `docker info` responda |
| `docker` no se encuentra en PowerShell | No está en el PATH de la sesión; recargarlo o abrir una terminal nueva |
| PostgreSQL no publica el puerto 5435 | Recrear solo ese servicio: `docker compose up -d --force-recreate --no-deps postgres`. Los datos están en el volumen |
| Una prueba de integración no se ejecuta en el CI | Le falta la marca de categoría. El pipeline sigue en verde sin haberla corrido, que es lo peligroso |
| Al cargar los DLL recién compilados, Windows responde que una política de control de aplicaciones lo bloquea | Smart App Control bloquea ensamblados nuevos sin reputación. Es la razón por la que las pruebas se ejecutan en contenedor: usar `./scripts/pruebas.ps1` (sección 6) en lugar de `dotnet test` directo |
| `./scripts/pruebas.ps1` avisa de que Docker no responde | Docker Desktop cerrado. Abrirlo y esperar a que `docker info` conteste, cerca de un minuto |
| `docker pull` falla con `not found` sobre una imagen del SDK | La etiqueta de la imagen no es la versión del SDK instalado. Las disponibles se consultan en `https://mcr.microsoft.com/v2/dotnet/sdk/tags/list` |
