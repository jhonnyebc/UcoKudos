# Diseño de alto nivel — modelo C4 niveles 1 y 2

Los diagramas usan la **notación C4** (tipo de elemento, tecnología, descripción y los colores estándar del modelo) dibujada sobre `flowchart` de Mermaid, no sobre la sintaxis `C4Context` nativa: esa última ignora el acomodo pedido y traza líneas por encima de las cajas, así que el resultado es ilegible. La notación se respeta; el motor de dibujo es el que cambia.

## 1. Nivel 1 — Diagrama de contexto

Qué es el sistema y con quién habla. Un solo bloque para UCO Kudos, las personas que lo usan y los sistemas externos de los que depende.

```mermaid
flowchart TB
    subgraph personas[" "]
        direction LR
        comunidad["<b>Comunidad académica</b><br/><i>[Persona]</i><br/>Estudiantes y docentes.<br/>Publican y reciben Kudos,<br/>responden y califican"]
        coordinacion["<b>Coordinación</b><br/><i>[Persona]</i><br/>Coordinadores de programa,<br/>de facultad y académico.<br/>Aprueban cuentas"]
        direccion["<b>Dirección</b><br/><i>[Persona]</i><br/>Rector, Coordinador Académico<br/>y Administrador.<br/>Administran categorías y vetan"]
    end

    kudos["<b>UCO Kudos</b><br/><i>[Sistema de software]</i><br/>Plataforma institucional de reconocimientos.<br/>Permite publicar Kudos con adjuntos, responder,<br/>calificar y moderar, y avisa al destinatario<br/>por tres canales"]

    identidad["<b>Proveedor de identidad</b><br/><i>[Sistema externo]</i><br/>Autentica con OIDC/OAuth2.<br/>Custodia las contraseñas"]
    meta["<b>WhatsApp Cloud API</b><br/><i>[Sistema externo]</i><br/>Entrega el aviso de Kudo<br/>por WhatsApp"]
    correo["<b>Proveedor de correo</b><br/><i>[Sistema externo]</i><br/>Entrega el aviso<br/>por correo electrónico"]
    push["<b>Servicio de push del navegador</b><br/><i>[Sistema externo]</i><br/>Entrega la notificación aunque<br/>la aplicación esté cerrada"]

    comunidad -->|"Publica, lee y responde Kudos<br/>[HTTPS]"| kudos
    coordinacion -->|"Aprueba solicitudes de cuenta<br/>[HTTPS]"| kudos
    direccion -->|"Administra categorías y veta<br/>[HTTPS]"| kudos

    kudos -->|"Delega la autenticación<br/>[OIDC/OAuth2]"| identidad
    kudos -->|"Envía la plantilla aprobada<br/>[HTTPS/Graph API]"| meta
    kudos -->|"Envía el correo<br/>[SMTP/TLS]"| correo
    kudos -->|"Envía la notificación<br/>[Web Push/VAPID]"| push

    meta -.->|"Mensaje de WhatsApp"| comunidad
    correo -.->|"Correo electrónico"| comunidad
    push -.->|"Notificación en el celular"| comunidad

    classDef persona fill:#08427b,stroke:#052e56,color:#ffffff
    classDef sistema fill:#1168bd,stroke:#0b4884,color:#ffffff
    classDef externo fill:#999999,stroke:#6b6b6b,color:#ffffff
    classDef invisible fill:none,stroke:none
    class comunidad,coordinacion,direccion persona
    class kudos sistema
    class identidad,meta,correo,push externo
    class personas invisible
```

### Qué se lee en este diagrama

| Observación | Consecuencia arquitectónica |
|---|---|
| Hay **un** sistema, no varios | No hay microservicios: es un monolito modular (ADR-0001) |
| La plataforma **no custodia contraseñas** | Toda una clase de vulnerabilidades queda fuera (ADR-0004) |
| Los tres canales de aviso son **sistemas externos**, lentos y falibles | Publicar un Kudo no puede depender de ellos: de ahí la bandeja de eventos (ADR-0005) |
| Los tres grupos de personas son **el mismo tipo de usuario** con distinto rol | No hay aplicaciones separadas por rol: una sola interfaz que muestra lo que corresponde (ADR-0004, ADR-0015) |
| Las flechas de vuelta son punteadas | El aviso llega **fuera** de la sesión del usuario: el destinatario puede no estar conectado (ADR-0009) |

## 2. Nivel 2 — Diagrama de contenedores

Las unidades que se despliegan y ejecutan, y cómo se comunican. Es el nivel donde se ven las decisiones de ADR-0005, ADR-0010, ADR-0012 y ADR-0016.

```mermaid
flowchart TB
    usuario["<b>Comunidad académica</b><br/><i>[Persona]</i>"]

    subgraph sistema["UCO Kudos"]
        direction TB
        pwa["<b>Cliente web</b><br/><i>[Contenedor: PWA, React + TypeScript]</i><br/>Interfaz mobile first.<br/>Service worker para Web Push<br/>e instalación en pantalla de inicio"]
        proxy["<b>Proxy inverso</b><br/><i>[Contenedor: Caddy]</i><br/>Único puerto expuesto.<br/>Termina TLS con certificado<br/>renovado automáticamente"]
        api["<b>API</b><br/><i>[Contenedor: ASP.NET Core]</i><br/>Casos de uso, autorización por<br/>jerarquía y lado de lectura.<br/>Expone SSE para avisos en vivo"]
        proceso["<b>Proceso de notificaciones</b><br/><i>[Componente en el mismo contenedor que la API]</i><br/>Recorre la bandeja de eventos con<br/>SKIP LOCKED y despacha los tres canales"]
        bd[("<b>Base de datos</b><br/><i>[Contenedor: PostgreSQL]</i><br/>Kudos, personas, jerarquía,<br/>bandeja de eventos y auditoría")]
        objetos[("<b>Almacenamiento de objetos</b><br/><i>[Contenedor: compatible con S3]</i><br/>Adjuntos. Contenedor privado:<br/>solo se sirve con URL prefirmada")]
        otel["<b>Recolector de observabilidad</b><br/><i>[Contenedor]</i><br/>Trazas, métricas y logs por OTLP.<br/>No se publica a internet"]
    end

    identidad["<b>Proveedor de identidad</b><br/><i>[Sistema externo]</i>"]
    meta["<b>WhatsApp Cloud API</b><br/><i>[Sistema externo]</i>"]
    correo["<b>Proveedor de correo</b><br/><i>[Sistema externo]</i>"]
    push["<b>Servicio de push</b><br/><i>[Sistema externo]</i>"]

    usuario -->|"Usa<br/>[HTTPS]"| proxy
    proxy -->|"Sirve los archivos estáticos"| pwa
    proxy -->|"Reenvía /api<br/>[HTTP interno]"| api
    pwa -->|"Consulta y publica<br/>[HTTPS/JSON]"| proxy
    pwa -->|"Inicia sesión<br/>[OIDC + PKCE]"| identidad

    api -->|"Lee y escribe en una transacción:<br/>Kudo, auditoría y evento<br/>[TCP/SQL]"| bd
    api -->|"Firma URL de vida corta<br/>[HTTPS/S3]"| objetos
    api -->|"Valida el token JWT<br/>[HTTPS]"| identidad
    api -->|"Exporta señales<br/>[OTLP]"| otel

    proceso -->|"Toma eventos pendientes<br/>[TCP/SQL]"| bd
    proceso -->|"Plantilla aprobada<br/>[HTTPS]"| meta
    proceso -->|"Correo<br/>[SMTP/TLS]"| correo
    proceso -->|"Notificación<br/>[Web Push/VAPID]"| push
    proceso -->|"Exporta señales<br/>[OTLP]"| otel

    pwa -->|"Sube el adjunto directo,<br/>sin pasar por la API<br/>[HTTPS/S3 prefirmado]"| objetos
    api -.->|"Avisos en vivo<br/>[SSE]"| pwa

    classDef persona fill:#08427b,stroke:#052e56,color:#ffffff
    classDef contenedor fill:#438dd5,stroke:#2e6295,color:#ffffff
    classDef almacen fill:#438dd5,stroke:#2e6295,color:#ffffff
    classDef externo fill:#999999,stroke:#6b6b6b,color:#ffffff
    class usuario persona
    class pwa,proxy,api,proceso,otel contenedor
    class bd,objetos almacen
    class identidad,meta,correo,push externo
```

### Contenedores

| Contenedor | Tecnología | Responsabilidad | ADR |
|---|---|---|---|
| Cliente web | PWA en React y TypeScript | Interfaz mobile first; service worker para Web Push | ADR-0015 |
| Proxy inverso | Caddy | Único puerto expuesto a internet; TLS automático; sirve los estáticos y reenvía la API | ADR-0016 |
| API | ASP.NET Core | Casos de uso, autorización por jerarquía, lado de lectura y SSE | ADR-0001, ADR-0004 |
| Proceso de notificaciones | Servicio en segundo plano | Recorre la bandeja y despacha los tres canales | ADR-0005, ADR-0006 |
| Base de datos | PostgreSQL | Única fuente de verdad, bandeja de eventos y auditoría | ADR-0003 |
| Almacenamiento de objetos | Compatible con S3 | Adjuntos, privados y servidos con URL prefirmada | ADR-0012 |
| Recolector de observabilidad | Recolector OTLP | Trazas, métricas y logs | ADR-0014 |

### Dos cosas de este diagrama que hay que saber defender

**El proceso de notificaciones vive dentro del contenedor de la API.** No es un contenedor aparte, y está dibujado así a propósito. Es un servicio en segundo plano del mismo proceso, que es lo que corresponde a un monolito modular (ADR-0001): separarlo exigiría otra imagen, otra unidad que desplegar y otra que vigilar, sin ningún driver que lo pida. El `SKIP LOCKED` de la bandeja ya permite que, si en el futuro se levantan varias instancias, ninguna procese el mismo evento que otra (QA-ELAS-01), así que la decisión no cierra la puerta a separarlo.

**El cliente sube los adjuntos directamente al almacenamiento**, no a través de la API, usando una URL prefirmada que la API emite **después** de comprobar permisos (ADR-0012). Así los megabytes de una foto no atraviesan la aplicación, que es lo que importa en la red móvil del 90 % de los usuarios (DRV-001). La autorización sigue estando en la API: el almacenamiento solo honra una firma.

## 3. Trazabilidad con los drivers

| Driver | Dónde se ve en estos diagramas |
|---|---|
| DRV-001 Movilidad | El cliente es una PWA, y la subida de adjuntos evita la API |
| DRV-002 Lectura masiva | La API tiene un lado de lectura propio, separado del de escritura |
| DRV-003 Multicanal | El proceso de notificaciones habla con los tres sistemas externos de aviso |
| DRV-004 Adjuntos | Almacenamiento de objetos como contenedor propio, privado |
| DRV-005 Seguridad | Un solo puerto expuesto; identidad delegada; almacenamiento no publicado |
| DRV-006 Observabilidad | El recolector recibe señales de la API y del proceso |
| DRV-007 Automatización | No aparece aquí: es el diagrama de despliegue el que muestra el pipeline |
