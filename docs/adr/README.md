# Architecture Decision Records — UCO Kudos

**Ingeniería del Software II** — Universidad Católica de Oriente, semestre 2-2026
**Autor:** Jhonny Estiven Benítez Caro · **Docente:** Wider Farid Sánchez Garzón
Trabajo individual. Fecha de las decisiones: 2026-11-08.

Los 19 ADR vigentes de la plataforma. Todos usan la misma estructura: **Metadatos, Contexto, Decisión, Alternativas consideradas, Consecuencias, Tácticas de Bass aplicadas, Escenarios y requisitos relacionados, ADR relacionados, Notas de implementación y Referencias.**

El vocabulario que los ADR referencian —drivers `DRV-`, requisitos `RF-`, escenarios de calidad `QA-` y restricciones `RT-`/`RN-`— está en [drivers-arquitectonicos.md](../drivers-arquitectonicos.md).

## Índice

| ADR | Decisión | Estado |
|---|---|---|
| [ADR-0001](adr-0001-monolito-modular-clean-architecture.md) | Monolito modular con Clean Architecture y reglas de dependencia verificadas | Accepted |
| [ADR-0002](adr-0002-csharp-dotnet-backend.md) | C# sobre .NET 10, reutilizando infraestructura propia ya probada | Accepted (rev. 2026-11-08) |
| [ADR-0003](adr-0003-postgresql-persistencia.md) | PostgreSQL con mapeo explícito y migraciones versionadas | Accepted |
| [ADR-0004](adr-0004-identidad-autorizacion-jerarquia.md) | Identidad delegada por OIDC; autorización por jerarquía institucional propia | Accepted |
| [ADR-0005](adr-0005-outbox-concurrencia-optimista.md) | Transactional Outbox y concurrencia optimista | Accepted |
| [ADR-0006](adr-0006-notification-gateway-multicanal.md) | Gateway de notificaciones multicanal con un adaptador por canal | Accepted |
| [ADR-0007](adr-0007-whatsapp-meta-cloud-api.md) | WhatsApp con Meta Cloud API, solo de salida y por plantilla aprobada | Accepted |
| [ADR-0008](adr-0008-correo-transaccional-smtp.md) | Correo transaccional por SMTP, con proveedor sustituible | Accepted |
| [ADR-0009](adr-0009-tiempo-real-sse-web-push.md) | SSE para usuarios conectados y Web Push (VAPID) para los desconectados | Accepted |
| [ADR-0010](adr-0010-lectura-masiva-cqrs-cache.md) | CQRS ligero, contadores materializados, caché por evento y paginación por cursor | Accepted |
| [ADR-0011](adr-0011-arbol-replicas-ruta-materializada.md) | Árbol de réplicas con referencia al padre y ruta materializada | Accepted |
| [ADR-0012](adr-0012-adjuntos-almacenamiento-objetos.md) | Adjuntos en almacenamiento de objetos privado con URL prefirmada | Accepted |
| [ADR-0013](adr-0013-auditoria-cadena-hashes.md) | Auditoría de solo inserción con cadena de hashes verificable | Accepted |
| [ADR-0014](adr-0014-observabilidad-opentelemetry.md) | OpenTelemetry con la traza propagada a través de la frontera asíncrona | Accepted |
| [ADR-0015](adr-0015-pwa-mobile-first-react.md) | Cliente como PWA mobile first en React con TypeScript | Accepted |
| [ADR-0016](adr-0016-despliegue-contenedores-maquina-unica.md) | Contenedores sobre una máquina de capa gratuita permanente, con HTTPS | Accepted |
| [ADR-0017](adr-0017-devsecops-quality-gates.md) | DevSecOps con quality gates que bloquean y pirámide de pruebas automatizada | Accepted |
| [ADR-0018](adr-0018-configuracion-secretos-twelve-factor.md) | Configuración por entorno y secretos fuera del repositorio (Twelve-Factor) | Accepted |
| [ADR-0019](adr-0019-alcance-linea-base.md) | Alcance de la línea base en tres niveles declarados | Accepted |

## Cómo leerlos

1. **Estructura de la solución:** ADR-0001 fija las capas y las reglas; ADR-0002 el lenguaje; ADR-0003 la persistencia.
2. **La funcionalidad obligatoria (RF-07):** ADR-0005 es el corazón (una transacción y una bandeja de eventos), ADR-0006 el punto único de salida, y ADR-0007, ADR-0008 y ADR-0009 los tres canales. ADR-0013 cierra el quinto paso con la auditoría.
3. **Los drivers del enunciado:** ADR-0010 responde a la alta demanda de lectura, ADR-0015 a la movilidad, ADR-0012 a los adjuntos, ADR-0014 a la observabilidad y ADR-0017 a la automatización.
4. **Gobierno:** ADR-0016 el despliegue, ADR-0018 la configuración y los secretos, ADR-0019 la frontera del alcance.

## Trazabilidad de los drivers del enunciado

| Driver | ADR que lo responden |
|---|---|
| DRV-001 Movilidad | ADR-0015, ADR-0010 |
| DRV-002 Alta demanda de lectura | ADR-0010, ADR-0011, ADR-0003 |
| DRV-003 Notificaciones multicanal | ADR-0006, ADR-0007, ADR-0008, ADR-0009 |
| DRV-004 Adjuntos | ADR-0012 |
| DRV-005 Seguridad | ADR-0004, ADR-0012, ADR-0017, ADR-0018 |
| DRV-006 Observabilidad | ADR-0013, ADR-0014 |
| DRV-007 Automatización | ADR-0017, ADR-0016 |

## Revisiones

| Fecha | ADR | Qué cambió |
|---|---|---|
| 2026-11-08 | ADR-0002 | La primera versión elegía Java con Spring Boot. Se revisó el mismo día al inventariar la infraestructura propia ya construida y probada, que resuelve el núcleo de RF-07. El contexto y las alternativas del ADR conservan el razonamiento completo. |

## Decisiones conscientemente aplazadas

Quedan registradas aquí para que no se confundan con omisiones:

| Tema | Decisión actual | Cuándo revisarla |
|---|---|---|
| Broker de mensajes | Sin broker: la cola es una tabla (ADR-0005) | Cuando haya varios consumidores o el volumen de eventos lo exija |
| Caché distribuida | En memoria del proceso (ADR-0010) | Al pasar a más de una instancia |
| Réplica de lectura | Sin réplica (ADR-0010) | Cuando se demuestre que el cuello de botella es la base |
| Número propio de WhatsApp | Número de prueba de Meta (ADR-0007) | Al disponer de verificación de empresa |
| Gestor de secretos dedicado | Archivo de entorno con permisos restringidos (ADR-0018) | Al existir presupuesto o necesidad de auditar accesos |
| Almacenamiento gestionado en la nube | Autoalojado compatible con S3 (ADR-0012) | Al existir presupuesto |
