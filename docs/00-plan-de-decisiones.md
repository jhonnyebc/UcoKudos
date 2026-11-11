# UCO Kudos — Plan de decisiones y ruta de trabajo

Fecha: 2026-11-08 · Entrega y sustentación: **vie 2026-11-13, 12:00** (quedan ~5 días)

## 1. Decisiones ya tomadas (ADR-001 a ADR-003)

> **ADR-001 revisado el 2026-11-08**, tras estudiar FlowRide (ver [01-reutilizacion-flowride.md](01-reutilizacion-flowride.md)). La versión anterior (Spring Boot + Angular) se tomó sin saber que FlowRide ya resolvía en código probado el flujo exacto que pide el enunciado. El usuario delegó la decisión.

| ADR | Decisión | Alternativas descartadas | Riesgo a vigilar |
|---|---|---|---|
| 001 Backend | **.NET 10 + C#**, reutilizando el núcleo de FlowRide: Outbox, gateway de notificaciones, adaptador de Meta Cloud API, auditoría con cadena SHA-256, CI con Sonar y Trivy, Dockerfile *chiseled* y reglas de arquitectura con ArchUnitNET | Spring Boot (habría que escribir todo eso desde cero en 5 días), NestJS | Que la reutilización arrastre conceptos multi-tenant que Kudos no necesita: hay que podarlos, no copiarlos |
| 001b Front-end | **React + Vite como PWA, mobile first**, con el tooling de FlowRide: tipos generados desde OpenAPI, Vitest con axe y la regla de lint contra secretos en `VITE_*` | Angular, reutilizar el panel de escritorio de FlowRide | El diseño es nuevo: el panel de FlowRide es de escritorio y Kudos es 90 % móvil |
| 001c Tiempo real | **SSE + Web Push (VAPID)**: SSE si el destinatario está conectado (reutiliza lo de FlowRide), Web Push si no | Solo SSE, solo Web Push | El permiso de notificaciones del navegador: hay que pedirlo en el momento adecuado |
| 002 Hosting | Una VM con Docker Compose y Caddy (HTTPS). Se intentó Oracle Cloud Always Free; su registro falló y se desplegó en Azure con crédito de estudiante (ver la revisión del 2026-11-11 en ADR-0016) | Render free (cold starts de 30-60 s), Railway/Fly (ya no son gratis) | Aprobación de cuenta: **crearla hoy** |
| 003 WhatsApp | Meta Cloud API tras un puerto `WhatsAppSender` | Twilio Sandbox (código de unión, expira a 3 días) | Token de prueba de 24 h: usar System User token. Hasta 5 destinatarios de prueba registrados |

## 2. Decisiones propuestas (por confirmar)

| ADR | Decisión propuesta | Por qué |
|---|---|---|
| 004 Estilo arquitectónico | Monolito modular con Clean Architecture (domain / application / infrastructure / interfaces) | 1 persona y 5 días: microservicios no se justifican. Los módulos se separan por paquete y se verifican con ArchUnit |
| 005 Mensajería | **Transactional Outbox en PostgreSQL con `FOR UPDATE SKIP LOCKED`**, sin broker aparte (adaptado de FlowRide) | Cumple "orientación a mensajes" y resiliencia sin sumar RabbitMQ: un contenedor menos en la VM y una pieza menos que explicar |
| 006 Lecturas masivas (DRV-002) | Modelo CQRS ligero: escritura normalizada; lecturas con caché Redis, paginación por cursor e índices. Réplica de lectura documentada como evolución | Lecturas >> escrituras. Se justifica con pruebas de carga simples |
| 007 Adjuntos | Almacenamiento compatible S3 (MinIO en la VM), URLs prefirmadas, lista de tipos permitidos, límite de tamaño, escaneo y metadatos con hash en BD | Cubre almacenamiento, seguridad, recuperación y trazabilidad que pide el enunciado |
| 008 Identidad | Auth0 (plan gratis, ya configurado en uco-test-api) para autenticación y JWT; roles y jerarquía en BD propia | Keycloak en la VM consume memoria y tiempo de configuración. FlowRide usa Keycloak, pero ahí lo justificaba un realm por operador, que Kudos no tiene |
| 009 Push en tiempo real | Ya decidido en ADR-001c: SSE + Web Push | — |
| 010 Correo | Brevo SMTP (300/día gratis) | Resend limita a 100/día |
| 011 Observabilidad | Micrometer + OpenTelemetry, Prometheus, Grafana, Loki; auditoría en tabla propia e inmutable | Todo en Docker Compose; la VM tiene RAM de sobra |
| 012 DevSecOps | GitHub Actions: CI (build, pruebas, SonarCloud, Trivy fs+imagen), CD (publica en GHCR y despliega por SSH) | SonarCloud es gratis con repos públicos. Repos privados: verificar |
| 013 Respuestas anidadas | Tabla con `parent_id` + CTE recursiva (o `ltree`) | Sin límite de profundidad |
| 014 Alcance de la línea base | Se implementa completo: publicar Kudo (5 pasos), login, categorías, roles semilla. Resto (aprobaciones, moderación, calificaciones) se **diseña y documenta**; se implementa solo si sobra tiempo | Prioriza lo que se evalúa |

## 3. Ruta de trabajo (hecha para 5 días)

| Día | Foco | Salida verificable |
|---|---|---|
| Dom 8 | Cuenta del proveedor de nube, **plantilla de WhatsApp enviada a aprobación**, números de prueba registrados, Brevo y SonarCloud; repos `kudos-api` y `kudos-web`; ADRs y drivers | Cuentas listas, esqueleto de repos |
| Lun 9 | Dominio + caso de uso Publicar Kudo + persistencia + Outbox + auditoría (adaptando FlowRide) | Pruebas unitarias en verde |
| Mar 10 | Correo, WhatsApp, SSE y Web Push; PWA React mobile first | Flujo completo en local |
| Mié 11 | CI/CD, Sonar, Trivy, despliegue en la nube, observabilidad | URL pública funcionando |
| Jue 12 | Pruebas de integración y E2E, evidencias, diagramas, matriz 12-Factor, manual | Documentación completa |
| Vie 13 | Revisión final y sustentación antes de las 12:00 | Entregado |

## 4. Qué haces tú hoy (yo no puedo hacerlo)

1. Crear la cuenta del proveedor de nube (es la de mayor riesgo de demora). Se intentó Oracle Cloud y acabó siendo Azure for Students: ver la revisión del 2026-11-11 en ADR-0016.
2. En **Meta for Developers** (ya tienes cuenta): **enviar a aprobación la plantilla de WhatsApp** del aviso de Kudo y registrar como destinatarios los números de la demostración. La plantilla es lo más urgente: el destinatario de un Kudo nunca te escribió, así que siempre estás fuera de la ventana de 24 h y sin plantilla aprobada no sale ningún mensaje.
3. Crear cuenta en **Brevo** y confirmar si **SonarCloud** ya tiene la organización `jhonnyebc` y el secreto `SONAR_TOKEN` (en la memoria de FlowRide quedó como pendiente opcional).
4. Crear los repositorios en GitHub.

Puertos para Kudos, para no chocar con FlowRide (5433 y 8080) ni con uco-test-api (5434 y 8081): PostgreSQL **5435** y API **8082**.

Los secretos (tokens, claves SMTP) los pegas tú en GitHub Secrets o en el `.env` de la VM; no los pases por el chat.

## 5. Fuentes consultadas

- Meta: precios de WhatsApp Business Platform (verificar el cambio de octubre 2026 en la página oficial).
- Twilio: documentación del Sandbox de WhatsApp.
- Angular: `SwPush`; librería `nl.martijndwars:web-push`.
- SonarCloud y `aquasecurity/trivy-action`.
- Comparativas de hosting gratuito y de correo transaccional (blogs de terceros: las cifras deben contrastarse con las páginas oficiales).
