# Qué se puede reutilizar de FlowRide en UCO Kudos

Fecha del estudio: 2026-11-08 · Fuente: `C:\Users\Lenovo\RiderProjects\FlowRide` (rama local, estado al 2026-10-28)

## 1. Qué es FlowRide hoy

Plataforma SaaS multi-tenant de transporte corporativo, **.NET 10 + C#**, monolito modular con puertos y adaptadores, panel web en React + TypeScript. Mismo curso (ISS0666), **mismo profesor (Wider Farid Sánchez Garzón)**, misma universidad.

Estado medido en el repo:

| Elemento | Estado |
|---|---|
| Pruebas | 556 unitarias (92,5 % líneas / 82,6 % ramas), 17 de arquitectura (ArchUnitNET), 146 de integración (Testcontainers con PostgreSQL, Keycloak, Mailpit y Azurite reales), 69 de frontend (Vitest + axe) |
| ADR | 20, todos con 10 secciones y una subsección de implementación |
| Diagramas | 17 en Mermaid, draw.io y PNG: C4 nivel 1 y 2, componentes, paquetes (dos versiones, una UML por espacio de nombres), 5 secuencias, despliegue C4, 6 de modelo ER |
| CI | 6 jobs: backend, frontend, gitleaks, **Trivy**, **SonarCloud**, imágenes + publicación en GHCR |
| Fases | 0 a 11 cerradas; la 12 (operación y despliegue) a medias |

## 2. El hallazgo que cambia el plan

**FlowRide ya resuelve, en código probado, el corazón exacto de la funcionalidad obligatoria de UCO Kudos.** El enunciado pide: persistir, enviar correo, enviar WhatsApp, notificar en tiempo real y auditar, todo en un flujo. FlowRide tiene ese mismo patrón funcionando:

```
Caso de uso → IUnidadTrabajo (una transacción: cambio + auditoría + evento Outbox)
                    ↓
        WorkerNotificaciones (FOR UPDATE SKIP LOCKED, reintentos con espera creciente)
                    ↓
        GatewayNotificaciones  →  WhatsApp (Meta Cloud API)
                               →  Correo (SMTP)
                               →  Alerta en vivo (SSE)
```

Eso es, pieza por pieza, lo que pide "Publicar Kudo" con sus 5 pasos.

## 3. Mapa de reutilización

### Reutilizable casi tal cual (adaptando nombres de dominio)

| Pieza de FlowRide | Para qué sirve en Kudos |
|---|---|
| `WorkerNotificaciones.cs` + `MensajeOutbox.cs` | Outbox transaccional con `FOR UPDATE SKIP LOCKED`, reintentos, descarte tras N intentos y eventos programados. Es el paso 1 y el 5 |
| `GatewayNotificaciones.cs` + `CatalogoMensajes` | Punto único de salida multicanal con deduplicación por `EventId`. Es el paso 2, 3 y 4 |
| `WhatsAppAdaptadores.cs` | Adaptador de Meta Cloud API: envío de texto y de plantilla, clasificación de errores transitorios y permanentes, y un adaptador **simulado** para las pruebas. Es el paso 3 |
| `IUnidadTrabajo` + auditoría con cadena SHA-256 | Confirma cambio, auditoría y evento en una sola transacción, con tabla de solo INSERT. Es el paso 5 |
| `.github/workflows/ci.yml` | Los 6 jobs. Cubre Sonar y Trivy, que son obligatorios, y además gitleaks |
| `Dockerfile` multi-etapa | Imágenes *chiseled* sin shell, usuario sin privilegios, etiquetas OCI con versión y commit. Ayuda a que Trivy pase limpio |
| Estilo de puertos y adaptadores | `CasosUso/`, `Entrada/{Puertos,Adaptadores,Dto}`, `Salida/Puertos`. **Ya acordado con este profesor** |
| Pruebas de arquitectura | `EstiloHexagonalTests` verifica las reglas de dependencia. Es evidencia directa de Clean Architecture |
| Plantilla de ADR | 10 secciones con tácticas de Bass, alternativas en tabla y consecuencias. Formato ya evaluado por este profesor |
| `docs/GUIA-WHATSAPP-META.md` | Los tropiezos ya resueltos: token de usuario del sistema, `subscribed_apps`, versión de ngrok |

### Reutilizable como plantilla, con contenido nuevo

Estructura del DAS, drivers arquitectónicos, restricciones técnicas y de negocio, trade-off de atributos de calidad, el `docs/README.md` como índice de entrega, y la forma de los diagramas Mermaid (C4 nivel 1 y 2, componentes, paquetes, secuencias, despliegue, ER).

**Advertencia importante:** los ADR y el DAS de FlowRide llevan tres autores (el equipo hasta el parcial). UCO Kudos es 100 % individual. Se reutiliza **el formato**, nunca el texto con los nombres de tus compañeros.

### Lo que NO sirve y hay que construir

| Falta | Por qué |
|---|---|
| **Web Push (VAPID + service worker)** | FlowRide notifica en vivo con SSE a un panel de escritorio abierto. El enunciado pide notificación PUSH. SSE sirve si el destinatario está conectado, pero conviene sumar Web Push |
| **Mobile first y PWA** | El panel de FlowRide es de escritorio para un operador logístico. Kudos es 90 % móvil. Es diseño nuevo |
| **Pruebas E2E** | FlowRide no tiene. El enunciado las exige en la pirámide |
| **Pipeline CD y URL pública** | FlowRide solo llegó a demostración local con HTTPS. Hay que montarlo |
| **Matriz Twelve-Factor** | No existe en FlowRide. El enunciado la pide factor por factor |
| **Análisis del Manifiesto Reactivo** | No existe como documento |
| **Dominio de Kudos** | Jerarquía de 7 roles, flujo de aprobaciones, réplicas recursivas sin límite de profundidad, calificaciones de 1 a 5, moderación y veto |
| **Alta demanda de lectura** | FlowRide tiene cache-aside (ADR-0006), pero el driver de lecturas masivas es propio de Kudos |

## 4. Dos simplificaciones que ganas

1. **WhatsApp en Kudos es solo de salida.** No hay bot ni conversación: solo avisar al destinatario. Eso elimina el webhook, la firma `X-Hub-Signature-256`, ngrok y la bandeja de entrada idempotente, que es lo que más trabajo dio en FlowRide. Queda únicamente `EnviarPlantillaAsync`.
2. **Un solo tenant.** Kudos es una universidad, no un SaaS multi-operador. Desaparecen el Silo por tenant, la CLI de aprovisionamiento y el borde común con YARP.

## 5. Riesgos detectados

| Riesgo | Detalle | Qué hacer |
|---|---|---|
| **Plantilla de WhatsApp** | Fuera de la ventana de 24 h, Meta exige plantilla aprobada. El destinatario de un Kudo nunca te escribió, así que **siempre** estás fuera de ventana | Solicitar hoy la aprobación de la plantilla |
| **Cinco destinatarios** | El número de prueba de Meta solo envía a números registrados en el portal | Registrar hoy los números de la demostración |
| **Token de 24 h** | El token temporal vence; FlowRide ya tropezó con esto | Usar token de usuario del sistema desde el principio |
| **SonarCloud** | En la memoria de FlowRide seguía como pendiente opcional. Verificar si la organización `jhonnyebc` y el `SONAR_TOKEN` existen | Confirmar hoy |
| **Red institucional** | Bloquea ngrok según la memoria de FlowRide | No lo necesitas si WhatsApp es solo de salida |
| **Choque de puertos** | PostgreSQL de FlowRide usa 5433 y el de uco-test-api también; Keycloak usa 8080 | Dar a Kudos puertos propios, por ejemplo 5435 y 8082 |

## 6. La decisión que esto obliga a revisar

Ayer se fijó Spring Boot + Angular por ser lo que ya usas en clase. Ese ADR se tomó **sin saber** que existía FlowRide.

| | .NET 10, reutilizando FlowRide | Spring Boot, desde cero |
|---|---|---|
| Outbox, gateway, WhatsApp, auditoría | Ya escritos y probados | Hay que escribirlos |
| CI con Sonar, Trivy y gitleaks | Copiar y ajustar | Hay que escribirlo |
| Reglas de arquitectura automatizadas | ArchUnitNET ya configurado | ArchUnit en Java, desde cero |
| Estilo aprobado por el profesor | Sí, y ya lo sustentaste | Sí, es el de `uco-test-api` |
| Lo entiendes para sustentar | Sí: tú tomaste cada decisión | Sí |
| Esfuerzo estimado en 5 días | Alcanzable | Muy apretado |

Con 5 días, reutilizar pesa más que la familiaridad con Java. Y la sustentación no sufre: ese código salió de decisiones tuyas documentadas en los ADR.
