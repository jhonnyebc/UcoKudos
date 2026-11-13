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

Comprobar que los cinco contenedores están arriba:

```bash
docker compose ps
```

| Servicio | Dirección en el equipo | Para qué |
|---|---|---|
| PostgreSQL | `localhost:5435` | Base de datos |
| Almacenamiento de objetos | `localhost:9000` | Adjuntos. No tiene consola web: se inspecciona con la API de S3 |
| Correo capturado | `localhost:8026` | Ver el correo sin enviarlo a nadie |
| Observabilidad | `localhost:18890` | Trazas, métricas y logs |
| API | `localhost:8082` | La aplicación. `/salud/vivo` y `/salud/listo` |

Los puertos están desplazados a propósito para poder tener varios entornos locales encendidos a la vez.

## 4. Arrancar la API

La API es un servicio más del `docker compose` del paso anterior, así que ya está arriba. Para reconstruirla después de cambiar código:

```bash
docker compose up -d --build api
```

Comprobar que vive:

```bash
curl http://localhost:8082/salud/vivo
```

Debe responder `{"estado":"vivo"}`. Y que además habla con la base:

```bash
curl http://localhost:8082/salud/listo
```

Debe responder `{"estado":"listo"}`. La diferencia importa: la primera dice que el proceso arrancó, la segunda que sus dependencias responden. Un contenedor «vivo» pero no «listo» es un servicio que acepta peticiones y falla todas.

### Por qué en contenedor y no con `dotnet run`

En este equipo `dotnet run` **no funciona**: muere al arrancar con `An Application Control policy has blocked this file (0x800711C7)`. Smart App Control bloquea la carga de los ensamblados recién compilados, igual que con las pruebas (sección 6) y con las migraciones (sección 5).

En otro equipo sin esa protección, `dotnet run` funcionaría; aun así el contenedor es la forma recomendada, porque es la misma que usa el servidor.

**Un detalle que cambia al pasar de uno a otro:** dentro de la red de Docker, los servicios se llaman por su nombre. La base es `postgres`, no `localhost`, y el correo es `mailpit`. Las direcciones `localhost:5435` y compañía son para conectarse **desde Windows**, no desde dentro de un contenedor.

## 5. Base de datos

Con los contenedores arriba, aplicar las migraciones:

```powershell
./scripts/migrar.ps1
```

Crea el esquema completo y siembra las 15 categorías y los 7 roles. Es idempotente: volver a ejecutarlo cuando no hay nada nuevo no hace nada.

Comprobar que quedó bien:

```powershell
docker compose exec -T postgres psql -U ucokudos -d ucokudos -c "\dt nucleo.*"
```

Deben aparecer cuatro esquemas en total: `nucleo` con las entidades principales, `notif` con la bandeja de salida y los avisos, `interaccion` con las réplicas y reacciones, y `audit` con la tabla de solo inserción.

### Por qué en un contenedor, otra vez

`dotnet ef database update` directo en este equipo falla con `Could not load assembly 'UcoKudos.Infrastructure'`. El mensaje es engañoso: el ensamblado existe y está referenciado. Lo que falla es **cargarlo**, por la misma razón que con las pruebas (sección 6): Smart App Control bloquea los ensamblados recién compilados sin reputación.

El script copia el código a una carpeta interna del contenedor en lugar de trabajar sobre el repositorio montado. La herramienta de migraciones compila por su cuenta y no respeta la carpeta de artefactos, así que escribiría `bin/` y `obj/` de Linux encima de los de Windows, y mezclarlos rompe las dos compilaciones porque los archivos intermedios guardan rutas absolutas.

La cadena de conexión se arma dentro del contenedor leyendo el `.env`, y viaja por variable de entorno: no se escribe en ningún archivo ni se imprime en la consola.

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


### 7.6 El correo en el token de acceso (ADR-0004)

Hay un paso del proveedor de identidad que no es evidente y que, si falta, hace que **todas** las peticiones autenticadas fallen con un error confuso.

ADR-0004 dice que el token aporta la identidad: el correo institucional. Pero un proveedor OIDC emite **dos** tokens con propósitos distintos: el de identidad, que es para el cliente y sí lleva el correo, y el de acceso, que es el que llega a la API y **no lo lleva**. Sin este paso, la API recibe como identidad algo como `google-oauth2|101592…` e intenta usarlo como dirección de correo.

Se arregla pidiéndole al proveedor que añada el correo como claim propio del token de acceso. En Auth0, en **Actions → Library → Create Action**, de tipo *Login / Post Login*:

```javascript
exports.onExecutePostLogin = async (event, api) => {
  // El nombre lleva espacio de nombres a propósito: un proveedor descarta los claims propios que
  // no lo tengan, para que no choquen con los del estándar.
  if (event.user.email) {
    api.accessToken.setCustomClaim('https://ucokudos.co/email', event.user.email);
  }
};
```

Después hay que **desplegarla y arrastrarla al flujo de Login**, en Actions → Flows → Login. Crearla sin añadirla al flujo no tiene ningún efecto, y es el descuido habitual.

El nombre del claim tiene que coincidir con el que lee `ContextoUsuario`. Si se cambia aquí, hay que cambiarlo allí.

**Una advertencia que ahorra tiempo:** el correo del proveedor tiene que corresponder a una persona registrada en la plataforma. Si no, la API responde que no existe una cuenta para esa dirección, y es el comportamiento correcto: autenticarse no es lo mismo que tener cuenta.
## 8. Despliegue en el servidor

Todo lo de esta sección se hace **una vez**. Después, desplegar una versión nueva es un comando.

### 8.1 La máquina

Una máquina virtual con Docker (ADR-0016). Con 1 GB de memoria va justo: conviene 2 GB o más, porque corren cinco contenedores y PostgreSQL no comparte de buena gana.

La instalación real usa **Azure for Students**, `Standard_B2as_v2` con 2 núcleos y 8 GB, Ubuntu 24.04 en West US 2. No es capa gratuita permanente sino crédito finito, así que **hay que desasignar la máquina cuando no se use**: detenerla desde dentro con `shutdown` no basta, porque queda reservada y se sigue cobrando. Hay que usar *Stop* en el portal, que la deja «Detenida (desasignada)».

Al crearla hay que **guardar la clave privada SSH**: sin ella no se entra, y no se puede volver a descargar.

Después, **abrir los puertos 80 y 443**, y esto tiene dos partes que se olvidan a menudo:

1. En la consola del proveedor, en las reglas de red de la subred.
2. **Dentro de la máquina**, en su cortafuegos, **solo en algunos proveedores**. Hay imágenes que traen reglas propias que bloquean todo aunque el proveedor lo permita, y el síntoma es desesperante: la máquina responde al ping y la web no carga. En el despliegue actual, sobre Azure, **no hizo falta**: basta con las reglas del grupo de seguridad. Los comandos de abajo son para el caso en que sí haga falta.

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
```

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
```

```bash
sudo netfilter-persistent save
```

### 8.2 El nombre de dominio

HTTPS necesita un nombre, no una IP: una autoridad certificadora no emite certificados para direcciones. Si no hay dominio propio, sirve un servicio de DNS comodín:

```text
kudos.<IP-de-la-maquina>.sslip.io
```

Ese nombre resuelve solo a esa IP y vale para un certificado de verdad. **Hay que comprobar que resuelve antes de levantar nada**, o el proxy pedirá el certificado, fallará y habrá que esperar antes de reintentar.

```bash
ping -c1 kudos.<IP>.sslip.io
```

### 8.3 Preparar la máquina

```bash
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-plugin git
```

```bash
sudo usermod -aG docker $USER && newgrp docker
```

```bash
git clone https://github.com/jhonnyebc/UcoKudos.git && cd UcoKudos
```

```bash
cp .env.produccion.example .env && chmod 600 .env
```

Rellenar el `.env` con los valores reales. Los permisos restringidos no son adorno: ese archivo tiene todos los secretos de la plataforma.

Y entrar al registro de imágenes, que es privado por omisión:

```bash
echo <token-de-github> | docker login ghcr.io -u jhonnyebc --password-stdin
```

### 8.4 Desplegar

```bash
./scripts/desplegar.sh 0.1.0-a1b2c3d 0.1.0-d4e5f6a
```

El argumento es la etiqueta que publicó el pipeline. **No se reconstruye nada en el servidor**: se despliega exactamente la imagen que pasó las puertas de calidad (ADR-0017).

El guion hace las cosas en este orden, y el orden importa: trae las imágenes, levanta la base, **aplica las migraciones**, publica los archivos del cliente, arranca la versión nueva y **comprueba que responde** antes de darse por bueno. Si la aplicación no contesta, termina con error en lugar de dejar un despliegue roto que parece correcto.

Las migraciones se aplican con la **misma imagen** que se va a desplegar, así que el esquema lo aplica exactamente el código que después lo va a usar.

### 8.5 Si el pipeline falla la mañana de la entrega

Hay que poder desplegar sin él. Construir y publicar la imagen a mano, desde el equipo:

```bash
docker build -t ghcr.io/jhonnyebc/ucokudos-api:0.1.0-manual .
```

```bash
docker push ghcr.io/jhonnyebc/ucokudos-api:0.1.0-manual
```

Y en el servidor, `./scripts/desplegar.sh 0.1.0-manual <etiqueta-del-cliente>`. Lo mismo con el cliente desde su repositorio, con la imagen `ucokudos-web`.

**Esto conviene probarlo antes y no el mismo día.** Un procedimiento de emergencia que nadie ha ejecutado nunca no es un procedimiento, es una intención.

### 8.6 Ver lo que no está expuesto

Solo el proxy publica puertos. Para mirar la base o el panel de observabilidad se abre un túnel desde el equipo:

```bash
ssh -L 5435:localhost:5435 -L 18890:localhost:18888 usuario@<IP>
```

Eso es deliberado: una base de datos accesible desde internet es la forma más rápida de perder los datos de todos.

### 8.7 Respaldo

La base y el almacenamiento se respaldan **juntos** (ADR-0012): un respaldo de la base sin los archivos deja Kudos que apuntan a adjuntos que ya no existen.

```bash
docker compose exec -T postgres pg_dump -U ucokudos ucokudos | gzip > respaldo-$(date +%F).sql.gz
```

```bash
docker run --rm -v ucokudos_ucokudos-objetos:/datos -v "$PWD":/salida alpine tar czf /salida/adjuntos-$(date +%F).tar.gz -C /datos .
```

**Y hay que probar la restauración al menos una vez.** Un respaldo que nunca se ha restaurado es un archivo del que no se sabe nada.

### 8.5 Despliegue que dispara el pipeline

Cada repositorio tiene un trabajo `desplegar` que, tras publicar la imagen escaneada desde `main`,
se conecta por SSH y despliega **solo su componente**: el de la API despliega la API y el del cliente
despliega el cliente, y cada uno deja como está la versión del otro. Sin la configuración de abajo el
trabajo no falla: termina con una **advertencia** que dice que la imagen se publicó y no se desplegó.

La llave del pipeline es una **llave de comando forzado**: en el servidor solo puede ejecutar
`scripts/desplegar-desde-pipeline.sh`, que acepta únicamente `desplegar api <etiqueta>` o
`desplegar web <etiqueta>` con una etiqueta publicada por el pipeline. No da terminal, ni reenvío de
puertos, ni forma de ejecutar otra cosa. Eso es lo que hace razonable guardarla en GitHub.

**Una sola vez, desde tu equipo** (no dentro del servidor). Primero, la llave:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/ucokudos_pipeline -N "" -C pipeline-ucokudos
```

Después, instalar la parte pública en el servidor **con su restricción** (un solo comando, sin pegar
nada en el servidor):

```bash
{ printf 'command="/home/azureuser/UcoKudos/scripts/desplegar-desde-pipeline.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding '; cat ~/.ssh/ucokudos_pipeline.pub; } | ssh -i ~/.ssh/ucokudos_key.pem azureuser@20.83.117.236 'cat >> ~/.ssh/authorized_keys'
```

El servidor debe tener el repositorio actualizado, porque el guion se trae con él:

```bash
ssh -i ~/.ssh/ucokudos_key.pem azureuser@20.83.117.236 'cd ~/UcoKudos && git pull && chmod +x scripts/desplegar-desde-pipeline.sh'
```

La huella del servidor, para que el pipeline no obedezca a un impostor:

```bash
ssh-keyscan -t ed25519 20.83.117.236 > /tmp/huella-ucokudos.txt
```

Y los cuatro secretos, en **cada** repositorio (con la CLI de GitHub, sin pegarlos en ninguna parte):

```bash
for repo in UcoKudos UcoKudosWeb; do
  gh secret set DESPLIEGUE_SSH_KEY -R jhonnyebc/$repo < ~/.ssh/ucokudos_pipeline
  gh secret set DESPLIEGUE_HUELLA_DEL_SERVIDOR -R jhonnyebc/$repo < /tmp/huella-ucokudos.txt
  gh secret set DESPLIEGUE_HOST -R jhonnyebc/$repo --body 20.83.117.236
  gh secret set DESPLIEGUE_USUARIO -R jhonnyebc/$repo --body azureuser
done
```

**Probarlo:** lanzar a mano el flujo de cualquiera de los dos repositorios (pestaña *Actions*,
*Run workflow* sobre `main`) y mirar el trabajo `desplegar`. Si el servidor no responde o rechaza la
llave, el trabajo falla con el error de SSH; si faltan secretos, termina con la advertencia.

**Si hay que revocarla:** borrar la línea `pipeline-ucokudos` de `~/.ssh/authorized_keys` en el
servidor. El despliegue a mano (sección 8.4) sigue funcionando igual.

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
| El gancho de pre-push bloquea diciendo que el escaneo no se pudo ejecutar | En Git Bash sobre Windows, la shell convierte las rutas del contenedor en rutas de Windows. El gancho ya desactiva esa conversión; si reaparece, comprobar que `MSYS_NO_PATHCONV=1` sigue en el comando de Docker |
| `docker compose` falla al descargar la imagen de MinIO con `pull access denied` o `401 UNAUTHORIZED` | **MinIO cerró sus imágenes públicas**, también las de Bitnami. El `docker-compose` ya usa otra implementación compatible con S3; si alguien lo revierte a MinIO, volverá a fallar. Anotado como revisión del 2026-11-09 en ADR-0012 |
| La compilación falla con `CS8034: Unable to load Analyzer assembly` sobre un paquete | Smart App Control bloquea el analizador por ser un ensamblado nuevo sin reputación. El de AWS ya se retira en `Directory.Build.props`; para otro paquete, añadir su nombre a ese mismo objetivo |
| **El job de escaneo de secretos falla en el CI diciendo `Unexpected exit code [1]`** | Mirar el log antes de alarmarse: si dice `no leaks found`, **no hay ningún secreto**. Ocurre en el push que **crea** una rama, porque la acción escanea el rango `<commit anterior>^..<commit nuevo>` y el primer commit del repositorio no tiene padre, así que git falla. No se repite en los push siguientes: se comprobó el 2026-11-09, cuando el push que creó `main` falló por esto y el de `develop`, con un rango válido, pasó |
