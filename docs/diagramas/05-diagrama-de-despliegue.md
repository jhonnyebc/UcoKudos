# Diagrama de despliegue

Dónde corre cada contenedor y cómo llega el código hasta ahí. Cubre ADR-0016 (infraestructura) y ADR-0017 (pipeline), y es el diagrama donde se ve DRV-007.

## 1. Despliegue en producción

Notación C4 de despliegue sobre `flowchart`: nodos de infraestructura con su tecnología, y dentro los contenedores que alojan.

```mermaid
flowchart TB
    subgraph movil["Dispositivo del usuario"]
        direction TB
        nav["<b>Navegador móvil</b><br/><i>[Nodo: Android o iOS]</i>"]
        pwa["<b>Cliente web</b><br/><i>[Contenedor: PWA]</i><br/>Instalable en la pantalla de inicio.<br/>Service worker activo aunque<br/>la aplicación esté cerrada"]
        nav --- pwa
    end

    subgraph nube["Proveedor de nube — capa gratuita permanente"]
        direction TB
        subgraph vm["<b>Máquina virtual</b><br/><i>[Nodo: Linux, Docker Compose]</i>"]
            direction TB
            caddy["<b>Proxy inverso</b><br/><i>[Contenedor: Caddy]</i><br/>ÚNICO puerto expuesto: 443.<br/>TLS renovado automáticamente.<br/>Sirve los estáticos y reenvía /api"]
            api["<b>API + proceso de notificaciones</b><br/><i>[Contenedor: imagen ucokudos-api]</i><br/>Imagen chiseled: sin consola ni gestor<br/>de paquetes, usuario sin privilegios.<br/>Etiqueta versión-sha, nunca latest"]
            pg[("<b>Base de datos</b><br/><i>[Contenedor: PostgreSQL]</i><br/>Volumen persistente")]
            s3[("<b>Objetos</b><br/><i>[Contenedor: compatible con S3]</i><br/>Volumen persistente.<br/>Contenedor privado")]
            otel["<b>Recolector</b><br/><i>[Contenedor]</i><br/>Solo 127.0.0.1:<br/>se ve por túnel SSH"]
        end
        env["<b>Archivo de entorno</b><br/><i>[Artefacto en la máquina]</i><br/>Secretos, permisos restringidos<br/>al usuario del servicio.<br/>NO versionado"]
    end

    registro["<b>Registro de contenedores</b><br/><i>[Nodo: GHCR]</i><br/>Imágenes versión-sha<br/>ya escaneadas"]
    identidad["<b>Proveedor de identidad</b><br/><i>[Sistema externo]</i>"]
    terceros["<b>Proveedores de aviso</b><br/><i>[Sistemas externos]</i><br/>WhatsApp Cloud API,<br/>correo y push"]

    pwa -->|"HTTPS 443"| caddy
    pwa -->|"OIDC + PKCE"| identidad

    caddy -->|"HTTP, red interna"| api
    api -->|"TCP, red interna"| pg
    api -->|"HTTPS, red interna"| s3
    api -->|"OTLP, red interna"| otel
    api -->|"Valida el token JWT"| identidad
    api -->|"HTTPS y SMTP salientes"| terceros
    api -.->|"lee al arrancar"| env

    registro -.->|"el pipeline despliega<br/>por SSH"| vm

    classDef nodo fill:#ffffff,stroke:#666666,color:#1a1a1a
    classDef contenedor fill:#438dd5,stroke:#2e6295,color:#ffffff
    classDef externo fill:#999999,stroke:#6b6b6b,color:#ffffff
    classDef artefacto fill:#fff6ee,stroke:#b5651d,color:#1a1a1a
    class movil,nube,vm nodo
    class nav,caddy,api,pg,s3,otel,pwa contenedor
    class identidad,terceros,registro externo
    class env artefacto
```

### Decisiones que se leen en el diagrama

| Observación | Razón |
|---|---|
| **Una sola máquina virtual**, no una plataforma de aplicaciones | Las plataformas gratuitas suspenden el servicio por inactividad y tardan de 30 a 60 segundos en despertar. Durante una sustentación eso es el peor momento posible (ADR-0016) |
| **Un solo puerto expuesto**, el 443 | La base, los objetos y el recolector escuchan solo en la red interna de los contenedores. Para ver el recolector se usa un túnel |
| La API y el proceso de notificaciones están en **el mismo contenedor** | Es un monolito modular: el proceso es un servicio en segundo plano del mismo artefacto (ADR-0001) |
| El cliente sube los adjuntos **directo** al almacenamiento | Los megabytes no atraviesan la API, que importa en la red móvil del 90 % de los usuarios (DRV-001, ADR-0012) |
| HTTPS es **obligatorio**, no una mejora | El service worker de la PWA y la API de notificaciones solo funcionan sobre un origen seguro |
| La imagen lleva etiqueta `versión-sha` | Se despliega exactamente la imagen que el pipeline construyó y escaneó, no una reconstruida (ADR-0017) |
| El archivo de entorno está **fuera** de la imagen | Un artefacto para todos los entornos; lo que cambia es la configuración (ADR-0018, factor V) |

**Un único punto de fallo y ninguna redundancia.** Si la máquina cae, la plataforma cae. Para el alcance de la entrega es aceptable y hay que declararlo; la mitigación es el respaldo probado de la base y del volumen de objetos, con el mismo procedimiento, porque un respaldo de uno sin el otro deja Kudos que apuntan a adjuntos inexistentes.

## 2. Entorno local

La misma topología, con dos cambios: el correo se atrapa en lugar de enviarse, y no hay proxy porque la API se ejecuta directamente. Esa paridad es el factor X de los doce (ADR-0018).

```mermaid
flowchart TB
    subgraph equipo["Equipo de desarrollo — Windows con Docker Desktop"]
        direction TB
        apiLocal["<b>API</b><br/><i>[Proceso: dotnet run]</i><br/>Puerto 8082.<br/>WhatsApp en modo Simulado"]

        subgraph compose["<b>docker compose</b><br/><i>[Nodo: contenedores locales]</i>"]
            direction LR
            pgL[("<b>PostgreSQL</b><br/>5435")]
            s3L[("<b>Objetos</b><br/>9000")]
            mailL["<b>Captura de correo</b><br/>8026<br/><i>Nada sale a internet</i>"]
            otelL["<b>Recolector</b><br/>18890"]
        end

        pruebas["<b>Contenedor de pruebas</b><br/><i>[Contenedor: SDK de .NET]</i><br/>Ejecuta las pruebas en Linux.<br/>Carpeta de artefactos propia"]
        front["<b>Cliente web</b><br/><i>[Proceso: servidor de desarrollo]</i>"]
    end

    apiLocal --> pgL
    apiLocal --> s3L
    apiLocal --> mailL
    apiLocal --> otelL
    front --> apiLocal
    pruebas --> pgL

    classDef nodo fill:#ffffff,stroke:#666666,color:#1a1a1a
    classDef contenedor fill:#438dd5,stroke:#2e6295,color:#ffffff
    class equipo,compose nodo
    class apiLocal,pgL,s3L,mailL,otelL,pruebas,front contenedor
```

**Las pruebas corren dentro de un contenedor.** No es una preferencia: en el equipo de desarrollo, Smart App Control está activo en modo bloqueo y rechaza cargar ensamblados recién compilados sin reputación, así que el ejecutor de pruebas muere antes de la primera prueba. El contenedor lo evita sin desactivar ninguna protección del sistema, que además sería irreversible. Tiene un segundo beneficio: es el mismo entorno Linux del pipeline, y de hecho el CI invoca el mismo script, así que un resultado local y uno del pipeline son comparables.

Los puertos están desplazados (5435 en lugar de 5432, 8082 en lugar de 8080) para poder tener varios entornos locales encendidos a la vez.

## 3. Del commit al servidor

El pipeline de ADR-0017. Todas las puertas bloquean: una puerta que solo informa es documentación, no una puerta.

```mermaid
flowchart TB
    dev["<b>Desarrollo</b><br/>commit en una rama"]
    hook["<b>Gancho pre-push</b><br/>Escaneo de secretos<br/><i>local, antes de enviar</i>"]
    pr["<b>Pull request</b><br/>hacia develop o main"]

    subgraph ci["<b>Pipeline de integración</b> — todas las puertas bloquean"]
        direction TB
        g1["Compilación sin advertencias<br/>y formato verificado"]
        g2["Pruebas unitarias<br/>+ umbral de cobertura del núcleo"]
        g3["Pruebas de arquitectura<br/><i>las 5 reglas de ADR-0001</i>"]
        g4["Pruebas de integración<br/><i>PostgreSQL y objetos reales<br/>en contenedores desechables</i>"]
        g5["Pruebas de punta a punta<br/><i>el flujo de RF-07 en un navegador</i>"]
        g6["Escaneo de secretos<br/><i>historial completo</i>"]
        g7["Trivy: dependencias<br/>y configuración"]
        g8["Análisis estático<br/>con la cobertura importada"]
        g9["Construir la imagen<br/>+ Trivy sobre la imagen"]
    end

    subgraph cd["<b>Pipeline de despliegue</b> — solo al integrar en main"]
        direction TB
        d1["Publicar en el registro<br/><i>la imagen YA escaneada,<br/>sin reconstruirla</i>"]
        d2["Aplicar migraciones<br/><i>antes de recibir tráfico</i>"]
        d3["Reemplazar el contenedor<br/>por SSH"]
        d4["Comprobar el punto<br/>de salud"]
    end

    prod["<b>URL pública</b><br/>funcionando"]

    dev --> hook
    hook -->|"sin secretos"| pr
    pr --> g1
    g1 --> g2 --> g3 --> g4 --> g5
    pr --> g6
    pr --> g7
    pr --> g8
    pr --> g9
    g5 --> cd
    g6 --> cd
    g7 --> cd
    g8 --> cd
    g9 --> cd
    d1 --> d2 --> d3 --> d4 --> prod

    classDef puerta fill:#eefaf0,stroke:#2d7a3e,color:#1a1a1a
    classDef paso fill:#eef6ff,stroke:#1168bd,color:#1a1a1a
    classDef hito fill:#438dd5,stroke:#2e6295,color:#ffffff
    class g1,g2,g3,g4,g5,g6,g7,g8,g9 puerta
    class d1,d2,d3,d4 paso
    class dev,hook,pr,prod hito
```

### Dos detalles del pipeline que conviene poder explicar

**Se escanea la imagen y se publica exactamente la imagen escaneada**, sin reconstruirla. Reconstruir después de escanear invalida el escaneo: la imagen que llega al servidor no sería la que pasó la puerta.

**El escaneo de secretos corre dos veces, y el que importa es el local.** Detectar un secreto en el pipeline significa que ya está en el repositorio remoto, y borrarlo del archivo no lo saca del historial. El gancho antes del envío es el que lo evita de verdad.

**Las acciones externas van fijadas por el identificador de commit, no por etiqueta.** Una etiqueta se puede mover a código distinto, y es un vector real de ataque a la cadena de suministro.

## 4. Riesgos del despliegue

| Riesgo | Mitigación |
|---|---|
| La creación de la cuenta en el proveedor puede exigir verificación y tardar | Es la primera tarea del desarrollo, antes que cualquier otra de infraestructura |
| La región elegida puede tener agotada la capacidad de la capa gratuita | Probar otra región |
| El pipeline falla la mañana de la entrega | Hay procedimiento manual de despliegue documentado |
| Una demostración que solo funciona en la red de casa | Probar el flujo completo desde un celular con datos móviles, fuera de esa red, al menos un día antes |
| Recursos limitados con cinco contenedores | Vigilar la memoria. Es la razón por la que ADR-0005 descartó añadir un broker de mensajes |
