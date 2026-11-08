# Drivers arquitectónicos — UCO Kudos

**Universidad Católica de Oriente** · Facultad de Ingenierías · Programa de Ingeniería de Sistemas
**Ingeniería del Software II** — semestre 2-2026
**Autor:** Jhonny Estiven Benítez Caro
**Docente:** Wider Farid Sánchez Garzón
Rionegro, Antioquia — noviembre de 2026

---

Este documento fija el vocabulario que usan los ADR: los drivers del enunciado, los requisitos funcionales, los atributos de calidad priorizados y los escenarios de calidad. Cada ADR referencia estos identificadores.

## 1. Drivers del enunciado

Los siete drivers vienen enunciados por el cliente y no se renumeran.

| ID | Driver | Qué condiciona |
|---|---|---|
| **DRV-001** | **Movilidad** — cerca del 90 % de los usuarios entra desde un celular | Estrategia Mobile First, peso del cliente, cantidad de peticiones por pantalla |
| **DRV-002** | **Alta demanda de lectura** — la carga es predominantemente de consulta | Modelo de lectura, caché, paginación, índices |
| **DRV-003** | **Notificaciones multicanal** — correo, WhatsApp y push | Un punto único de salida con un adaptador por canal |
| **DRV-004** | **Adjuntos** — los Kudos admiten archivos | Almacenamiento fuera de la base, seguridad y trazabilidad del archivo |
| **DRV-005** | **Seguridad** | Autenticación, autorización por jerarquía, secretos, validación de entrada |
| **DRV-006** | **Observabilidad** — monitoreo, métricas, trazas y auditoría | Instrumentación desde la línea base, no añadida al final |
| **DRV-007** | **Automatización** — DevSecOps desde la línea base | Quality gates en cada cambio, no una revisión manual al cierre |

## 2. Atributos de calidad priorizados

Escala de 8 a 1, donde 8 es la mayor prioridad relativa. La prioridad sale de los drivers: movilidad y lectura masiva son los dos que el cliente declaró explícitamente con cifras.

| Prioridad | Atributo | Aplicación en UCO Kudos |
|---|---|---|
| **8** | **Rendimiento** | El muro de Kudos es la pantalla más visitada y se abre desde un celular en red móvil. Objetivo: P95 < 500 ms y P99 < 1,5 s en las consultas del muro |
| **7** | **Seguridad** | Jerarquía de 7 roles con reglas de exclusividad y unicidad, adjuntos que no deben quedar públicos, y tres integraciones externas con credenciales |
| **6** | **Confiabilidad** | Publicar un Kudo dispara cuatro efectos (persistir, correo, WhatsApp, push) más la auditoría: ninguno puede perderse ni duplicarse |
| **5** | **Disponibilidad** | Que WhatsApp o el correo estén caídos no puede impedir publicar un Kudo |
| **4** | **Usabilidad** | Mobile First real: se usa con una mano, en pantalla pequeña y con red intermitente |
| **3** | **Mantenibilidad** | Cambiar de proveedor de correo o de WhatsApp no debe tocar el dominio |
| **2** | **Observabilidad** | Poder reconstruir qué pasó en una publicación concreta, por exigencia del enunciado y para sustentar |
| **1** | **Elasticidad** | Hoy una sola instancia; el diseño no debe impedir escalar horizontalmente |

## 3. Requisitos funcionales

| ID | Requisito | Significativo |
|---|---|---|
| RF-01 | Registro con correo institucional y número celular | Sí — define la identidad |
| RF-02 | Activación de cuenta: solicitud aprobada + correo verificado + celular verificado | Sí — máquina de estados de la cuenta |
| RF-03 | Flujo de aprobaciones según la jerarquía institucional | Sí — regla de autorización |
| RF-04 | Roles con exclusividad (un rol activo por persona) y unicidad (un Rector, un Coordinador Académico, uno por facultad y por programa) | Sí — invariante de dominio |
| RF-05 | Gestión académica: programas de un estudiante, programas de un docente | No |
| RF-06 | Administración de categorías, solo Administrador, Rector y Coordinador Académico | Sí — regla de autorización |
| **RF-07** | **Publicar Kudo**: persistir, correo, WhatsApp, push en tiempo real y auditoría | **Sí — es la funcionalidad obligatoria de la línea base** |
| RF-08 | Adjuntos en los Kudos | Sí — DRV-004 |
| RF-09 | Réplicas sobre Kudos y sobre réplicas, sin límite de profundidad ni de cantidad | Sí — decide el modelo de datos |
| RF-10 | Calificación de 1 a 5 estrellas, una activa por usuario y contenido | No |
| RF-11 | Reportar contenido, disponible para todos los usuarios | No |
| RF-12 | Vetar contenido, solo Rector y Coordinador Académico | Sí — regla de autorización |
| RF-13 | Consultar el muro de Kudos | Sí — DRV-002 |
| RF-14 | Notificación por correo electrónico | Sí — DRV-003 |
| RF-15 | Notificación por WhatsApp | Sí — DRV-003 |
| RF-16 | Notificación push en tiempo real si el destinatario está conectado | Sí — DRV-003 |
| RF-17 | Registro de auditoría del proceso de publicación | Sí — DRV-006 |
| RF-18 | Métricas, trazas y logs de la plataforma | Sí — DRV-006 |

## 4. Escenarios de calidad

Formato: estímulo, entorno, respuesta y medida.

| ID | Escenario | Medida |
|---|---|---|
| **QA-REND-01** | Un estudiante abre el muro desde un celular en red móvil con 10 000 Kudos en la base | P95 < 500 ms y P99 < 1,5 s en el servidor; primera pantalla útil < 2,5 s en el cliente |
| **QA-REND-02** | 200 usuarios consultan el muro al mismo tiempo durante la semana de reconocimientos | Sin degradación por encima de QA-REND-01; la lectura no toca la ruta de escritura |
| **QA-MOV-01** | Un docente publica un Kudo desde un celular de gama media | La pantalla funciona con una mano, sin desplazamiento horizontal, y el peso inicial del cliente no supera los 300 KB comprimidos |
| **QA-CONF-01** | La transacción que guarda el Kudo confirma y el proceso se cae antes de notificar | Ninguna notificación se pierde: al reiniciar, el evento pendiente se procesa |
| **QA-CONF-02** | El mismo evento se procesa dos veces por un reintento | El destinatario recibe **un** correo y **un** WhatsApp: deduplicación por identificador de evento |
| **QA-DISP-01** | La Graph API de Meta responde 503 al publicar un Kudo | El Kudo queda publicado y auditado; el WhatsApp se reintenta con espera creciente y, si agota intentos, queda registrado el fallo |
| **QA-SEG-01** | Un Coordinador de Programa intenta crear una categoría | La plataforma responde 403 y el intento queda auditado |
| **QA-SEG-02** | Alguien con la URL de un adjunto intenta abrirla una semana después | El enlace está vencido: los adjuntos solo se sirven con URL prefirmada de vida corta |
| **QA-SEG-03** | Se intenta registrar un segundo Rector activo | La plataforma lo rechaza por invariante, no por validación de interfaz |
| **QA-OBS-01** | Hay que explicar qué pasó en una publicación concreta de hace dos días | Se reconstruye el flujo completo con un solo identificador de correlación: traza, logs y eventos de auditoría |
| **QA-MANT-01** | Hay que cambiar el proveedor de correo | Se implementa un adaptador nuevo del puerto de salida; el dominio y los casos de uso no cambian |
| **QA-ELAS-01** | Se levanta una segunda instancia de la API | Dos instancias procesan la misma cola de eventos sin duplicar envíos |

## 5. Restricciones

### Técnicas

| ID | Restricción | Origen |
|---|---|---|
| RT-01 | La documentación se entrega en Markdown | Enunciado |
| RT-02 | Repositorio de front-end y de back-end separados | Enunciado |
| RT-03 | Sonar y Trivy obligatorios en el pipeline | Enunciado |
| RT-04 | Pipeline CI y pipeline CD funcionales | Enunciado |
| RT-05 | La solución debe estar publicada en una URL pública que el docente pueda usar | Enunciado |
| RT-06 | Debe evidenciar Clean Architecture, SOLID, Clean Code, patrones, Manifiesto Reactivo, Twelve-Factor y pirámide de pruebas | Enunciado |
| RT-07 | Presupuesto de infraestructura cero: solo capas gratuitas | Decisión del autor |

### De negocio

| ID | Restricción | Origen |
|---|---|---|
| RN-01 | Trabajo 100 % individual: todo debe poder ser explicado y sustentado por el autor | Enunciado |
| RN-02 | Fecha límite inamovible: viernes 13 de noviembre de 2026, 12:00 | Enunciado |
| RN-03 | El contenido de un Kudo y de una réplica no tiene límite de longitud | Enunciado |
| RN-04 | Cada persona tiene un único rol activo | Enunciado |

## 6. Trazabilidad de drivers a decisiones

| Driver | ADR que lo responden |
|---|---|
| DRV-001 Movilidad | ADR-0015 (PWA mobile first), ADR-0010 (menos peticiones por pantalla) |
| DRV-002 Lectura masiva | ADR-0010 (CQRS ligero y caché), ADR-0011 (árbol de réplicas), ADR-0003 (índices) |
| DRV-003 Multicanal | ADR-0006 (gateway), ADR-0007 (WhatsApp), ADR-0008 (correo), ADR-0009 (push) |
| DRV-004 Adjuntos | ADR-0012 (almacenamiento de objetos) |
| DRV-005 Seguridad | ADR-0004 (identidad), ADR-0018 (secretos), ADR-0012 (URL prefirmada), ADR-0017 (escaneos) |
| DRV-006 Observabilidad | ADR-0013 (auditoría), ADR-0014 (OpenTelemetry) |
| DRV-007 Automatización | ADR-0017 (quality gates), ADR-0016 (despliegue) |
