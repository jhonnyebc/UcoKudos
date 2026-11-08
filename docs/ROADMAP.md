# Plan de desarrollo por fases

**Punto de partida:** domingo 2026-11-08, 17:39 · **Entrega, despliegue y sustentación:** viernes 2026-11-13, 12:00
**Tiempo disponible:** 114 horas de calendario, unos 5 días de trabajo.

El alcance de cada fase respeta ADR-0019: el nivel 1 es intocable y el nivel 2 solo entra con el nivel 1 desplegado y con sus evidencias recogidas.

## Regla de oro del plan

**Cada fase termina con algo verificable, no con algo escrito.** Una fase no se cierra porque se acabó el día, sino porque su criterio de cierre se cumple. Si una fase se retrasa, lo que se recorta es el nivel 2 (Fase 6), nunca las evidencias ni el despliegue.

---

## Fase 0 — Documentación y línea base del backend
**Domingo 8, tarde y noche**

| Entregable | Estado |
|---|---|
| 19 ADR, drivers, ABB y SBB | Hecho |
| DAS, 21 diagramas validados, modelo de datos | Hecho |
| Twelve-Factor, Manifiesto Reactivo, desarrollo seguro, patrones, pirámide | Hecho |
| Manual de puesta a punto | Hecho |
| Estructura de 4 capas con pruebas de arquitectura y CI | Hecho |
| **Backend de la línea base**: dominio, aplicación, infraestructura y API | En curso |

**Criterio de cierre:** compila sin advertencias, formato limpio, pruebas unitarias y de arquitectura en verde, y ninguna carpeta de capa vacía.

---

## Fase 1 — Cuentas externas y persistencia real
**Lunes 9**

Lo primero del día son las tres tareas que **dependen de terceros**, porque son las únicas que no se pueden acelerar trabajando más.

### 1.a Tareas con dependencia externa (primera hora del día)

| Tarea | Por qué primero |
|---|---|
| **Enviar la plantilla de WhatsApp a aprobación de Meta** | Riesgo P-01 del DAS. El destinatario de un Kudo nunca escribió primero, así que **todos** los envíos van por plantilla: sin aprobación no sale ni un mensaje. La aprobación no es inmediata |
| **Registrar los números de prueba** en el portal de Meta, incluido el del docente si va a recibir un Kudo en vivo | El número de prueba solo envía a destinatarios registrados |
| **Crear la cuenta del proveedor de nube** | Riesgo P-02. Puede exigir verificación y tardar |
| Crear cuenta de correo transaccional, proyecto en Sonar y el secreto del token | Bloquean el pipeline |

### 1.b Persistencia real

- Mapeo de EF Core de todas las entidades, con los índices únicos parciales de RF-04 y los índices del muro.
- Migración inicial, con las 15 categorías y los 7 roles sembrados.
- Aplicarla al entorno local y comprobar el esquema.
- Repositorios y unidad de trabajo contra PostgreSQL real.

### 1.c Primeras pruebas de integración

Las tres no negociables de la pirámide:

1. Un reproceso no duplica notificaciones (QA-CONF-02).
2. El segundo Rector activo es imposible (QA-SEG-03).
3. Dos procesos compitiendo por la cola producen cero envíos duplicados (QA-ELAS-01).

**Criterio de cierre:** la plantilla enviada a Meta, la cuenta de nube creada, la migración aplicada en local y las pruebas de integración en verde.

---

## Fase 2 — Los tres canales y el flujo obligatorio de punta a punta
**Martes 10**

Es el día de la funcionalidad que el enunciado exige. Al final del día, publicar un Kudo debe producir los cinco efectos **en local**.

| Paso | Qué se conecta |
|---|---|
| 1. Persistencia | Ya en la Fase 1 |
| 2. Correo | Adaptador SMTP contra el contenedor de captura, con el HTML revisado **en pantalla de celular** |
| 3. WhatsApp | Adaptador de Meta con el token de usuario del sistema y la plantilla aprobada. Primera prueba real a un número registrado |
| 4. Tiempo real | SSE para conectados y Web Push con VAPID para desconectados |
| 5. Auditoría | Cadena de hashes, con la operación de verificación funcionando |

**Criterio de cierre:** se publica un Kudo en local y llegan el correo, el WhatsApp a un celular real y la notificación push. La verificación de la cadena de auditoría pasa.

**Si algo se cae aquí, es el día de recuperarlo**, porque la Fase 3 depende de que el flujo funcione.

---

## Fase 3 — Cliente web mobile first
**Miércoles 11, primera mitad**

- Repositorio del front-end (RT-02), PWA en React con TypeScript.
- Tipos generados del contrato OpenAPI.
- Pantallas del nivel 1: iniciar sesión, muro con desplazamiento infinito por cursor, publicar Kudo con adjunto, detalle de un Kudo, avisos.
- Service worker y suscripción a Web Push.
- Los tres presupuestos de QA-MOV-01, verificados y bloqueantes.
- Pruebas de componentes con verificación de accesibilidad.

**Criterio de cierre:** el flujo completo se hace desde un celular, con la interfaz usable con una mano y sin desplazamiento horizontal.

---

## Fase 4 — Despliegue y observabilidad
**Miércoles 11, segunda mitad, y jueves 12 por la mañana**

- Máquina virtual con Docker Compose, proxy inverso y HTTPS.
- Archivo de entorno con permisos restringidos, con los secretos puestos por el autor desde su terminal.
- Pipeline de despliegue: publicar la imagen escaneada, aplicar migraciones, reemplazar el contenedor y comprobar el punto de salud.
- Observabilidad en pie: trazas, métricas, logs y la traza que cruza la frontera asíncrona (QA-OBS-01).
- Respaldo de base y objetos, **y una restauración probada**.

**Criterio de cierre, y es el más importante de todo el plan:** el docente abre la URL pública desde un celular con datos móviles, **fuera de la red de casa**, publica un Kudo y le llegan los tres avisos.

---

## Fase 5 — Evidencias y pruebas de la cima
**Jueves 12**

Las evidencias se recogen **a medida que se generan**, no al final. Esta fase es para completar y ordenar.

| Evidencia | Cómo se obtiene |
|---|---|
| CI y CD | Capturas de ejecuciones en verde |
| Sonar | Captura del tablero con la cobertura |
| Trivy | Informes de dependencias y de imagen |
| Correo, WhatsApp, Push | Capturas de la bandeja, del celular y de la notificación en pantalla bloqueada |
| Auditoría | La operación de verificación de la cadena, ejecutada en vivo |
| Observabilidad | La traza completa de una publicación, con su identificador |
| Pruebas | Resumen de los cuatro niveles y el reporte de cobertura |
| Prueba de carga | Percentiles de QA-REND-01 y QA-REND-02 |

Además: pruebas de punta a punta del flujo obligatorio, y la prueba de carga con datos sembrados.

**Criterio de cierre:** las doce evidencias que pide el enunciado están recogidas y ordenadas en la carpeta de entrega.

---

## Fase 6 — Nivel 2, solo con todo lo anterior cerrado
**Jueves 12, si sobra tiempo**

En este orden, que no es arbitrario:

1. **Réplicas anidadas** (RF-09). Primero porque ADR-0011 es de las decisiones más interesantes de sustentar y el modelo de datos ya lo contempla.
2. **Calificaciones** (RF-10). Barata: una tabla, un índice único y un contador que ya existe.
3. **Flujo de aprobaciones** (RF-03). El servicio de la jerarquía ya responde quién aprueba a quién; falta la máquina de estados y sus pantallas.
4. **Reportar y vetar** (RF-11, RF-12). El veto en cascada necesita que existan las réplicas.

**Regla:** una funcionalidad incompleta **no se despliega**. Es peor que ausente, porque el docente la encontrará y fallará.

---

## Fase 7 — Revisión y sustentación
**Viernes 13, hasta las 12:00**

**Este día no se desarrolla.** Se revisa.

- Repaso de la URL pública: iniciar sesión, publicar, ver los avisos.
- Repaso de los documentos y de que el estado declarado coincida con lo implementado.
- Ensayo de la sustentación, con las cuatro cosas que el enunciado avisa que pueden pedirse: explicar decisiones, implementar en vivo, cambiar código y refactorizar.
- Tener a mano: la operación de verificación de la auditoría, la traza de una publicación y la prueba de arquitectura que detecta una violación. Son las tres demostraciones más fuertes.

---

## Resumen

| Fase | Cuándo | Criterio de cierre |
|---|---|---|
| 0 | Dom 8 tarde | Documentación completa y backend de la línea base compilando con pruebas en verde |
| 1 | Lun 9 | Plantilla enviada a Meta, cuenta de nube creada, migración aplicada, integración en verde |
| 2 | Mar 10 | Los cinco efectos de publicar un Kudo funcionando en local |
| 3 | Mié 11 a.m. | El flujo completo desde un celular |
| 4 | Mié 11 p.m. y Jue 12 a.m. | **URL pública funcionando desde datos móviles, fuera de la red de casa** |
| 5 | Jue 12 | Las doce evidencias recogidas |
| 6 | Jue 12, con margen | Nivel 2 en el orden dado, sin dejar nada a medias |
| 7 | Vie 13 a.m. | Revisado y sustentado |

## Las tres cosas que pueden hundir el plan

1. **La plantilla de WhatsApp.** Es lo único que no depende del autor. Va la primera hora del lunes.
2. **El despliegue.** Si la URL pública no está en pie, no hay entrega, por buena que sea la documentación. Por eso tiene día y medio y no una tarde.
3. **Dejar las evidencias para el final.** Se recogen a medida que se generan. Si el tiempo se agota el jueves, la entrega ya tiene que estar completa.
