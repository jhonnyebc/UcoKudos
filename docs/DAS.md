# Documento de Arquitectura de Software — UCO Kudos

**Universidad Católica de Oriente** · Facultad de Ingenierías · Programa de Ingeniería de Sistemas
**Ingeniería del Software II** — semestre 2-2026
**Autor:** Jhonny Estiven Benítez Caro
**Docente:** Wider Farid Sánchez Garzón
Rionegro, Antioquia — noviembre de 2026

Versión 1.1 — **revisada el 2026-11-13 contra el sistema desplegado**: las afirmaciones que se pueden comprobar se contrastaron con el código, las pruebas y la producción, y lo que no resistió se corrigió o se declaró.

---

## Tabla de contenido

1. [Introducción](#1-introducción)
2. [Motivadores arquitectónicos](#2-motivadores-arquitectónicos)
3. [Tácticas aplicadas](#3-tácticas-aplicadas)
4. [Diseño de alto nivel](#4-diseño-de-alto-nivel)
5. [Architecture y Solution Building Blocks](#5-architecture-y-solution-building-blocks)
6. [Diseño detallado](#6-diseño-detallado)
7. [Clean Architecture](#7-clean-architecture)
8. [Manifiesto de Sistemas Reactivos](#8-manifiesto-de-sistemas-reactivos)
9. [The Twelve-Factor App](#9-the-twelve-factor-app)
10. [Patrones, SOLID y Clean Code](#10-patrones-solid-y-clean-code)
11. [Desarrollo seguro](#11-desarrollo-seguro)
12. [Estrategia de pruebas](#12-estrategia-de-pruebas)
13. [DevSecOps y despliegue](#13-devsecops-y-despliegue)
14. [Observabilidad y auditoría](#14-observabilidad-y-auditoría)
15. [Decisiones de arquitectura](#15-decisiones-de-arquitectura)
16. [Alcance y estado](#16-alcance-y-estado)
17. [Riesgos](#17-riesgos)

---

## 1. Introducción

### 1.1 Qué es UCO Kudos

Plataforma institucional de reconocimientos de la Universidad Católica de Oriente. Cualquier integrante de la comunidad académica publica un **Kudo** para otra persona, clasificado en una de las categorías institucionales, con contenido descriptivo y archivos adjuntos. El destinatario se entera por correo, por WhatsApp y por una notificación en su celular, y todo el proceso queda auditado.

Alrededor de ese flujo hay una estructura organizacional de siete roles con reglas estrictas de exclusividad y unicidad, un flujo de aprobaciones en cadena, réplicas anidadas sin límite de profundidad, calificaciones y moderación.

### 1.2 Qué es este documento

El documento integrador de la arquitectura. No repite lo que ya está desarrollado en otros documentos de esta carpeta: lo articula y remite a ellos. Cada sección dice dónde está el detalle.

| Si busca | Vaya a |
|---|---|
| El razonamiento de una decisión concreta | El ADR correspondiente, en [adr/](adr/README.md) |
| Los drivers, requisitos y escenarios de calidad | [drivers-arquitectonicos.md](drivers-arquitectonicos.md) |
| Los diagramas con su explicación | [diagramas/](diagramas/) |
| Cómo levantar la plataforma | [MANUAL-PUESTA-A-PUNTO.md](MANUAL-PUESTA-A-PUNTO.md) |

### 1.3 Qué distingue a esta arquitectura

Tres cosas, por si solo se lee esta página:

1. **El flujo obligatorio se resuelve con una bandeja de eventos transaccional**, no con llamadas síncronas a tres proveedores. Esa decisión (ADR-0005) es la que hace que publicar un Kudo responda rápido, que ninguna notificación se pierda si el proceso muere, y que WhatsApp caído no impida publicar.
2. **La jerarquía institucional vive en el dominio**, no en los claims de un token ni en la configuración de un proveedor de identidad. Es la parte más característica del enunciado y es la que se puede probar sin levantar nada (ADR-0004).
3. **Las reglas de la arquitectura son verificables.** Clean Architecture no es una afirmación de este documento: hay pruebas que fallan si el dominio referencia infraestructura, y la regla que el compilador no puede garantizar se comprobó provocando a propósito una violación.

---

## 2. Motivadores arquitectónicos

Desarrollo completo en [drivers-arquitectonicos.md](drivers-arquitectonicos.md).

### 2.1 Drivers

Los siete del enunciado. Dos vienen cuantificados por el cliente y son los que más condicionan el diseño:

| ID | Driver | Peso |
|---|---|---|
| **DRV-001** | **Movilidad**: cerca del 90 % de los usuarios desde celular, con estrategia Mobile First | Decisivo |
| **DRV-002** | **Alta demanda de lectura**: carga predominantemente de consulta | Decisivo |
| DRV-003 | Notificaciones multicanal: correo, WhatsApp y push | Alto |
| DRV-004 | Adjuntos en los Kudos | Medio |
| DRV-005 | Seguridad | Alto |
| DRV-006 | Observabilidad | Medio |
| DRV-007 | Automatización DevSecOps desde la línea base | Alto |

### 2.2 Atributos de calidad priorizados

| Prioridad | Atributo | Por qué ahí |
|---|---|---|
| 8 | Rendimiento | El muro es la pantalla más visitada y se abre en red móvil |
| 7 | Seguridad | Siete roles con ámbito, adjuntos de personas, tres integraciones con credenciales |
| 6 | Confiabilidad | Publicar dispara cuatro efectos más la auditoría: ninguno puede perderse ni duplicarse |
| 5 | Disponibilidad | Un canal caído no puede impedir publicar |
| 4 | Usabilidad | Mobile First real, no «que quepa en el celular» |
| 3 | Mantenibilidad | Cambiar de proveedor sin tocar el dominio |
| 2 | Observabilidad | Reconstruir qué pasó en una publicación concreta |
| 1 | Elasticidad | Hoy una instancia; el diseño no debe impedir escalar |

### 2.3 Restricciones

**Técnicas:** documentación en Markdown, repositorios separados de front y back, Sonar y Trivy obligatorios, pipelines de CI y CD, URL pública usable por el docente, y presupuesto de infraestructura cero.

**De negocio:** trabajo 100 % individual y todo explicable por el autor; fecha límite inamovible; contenido sin límite de longitud; un rol activo por persona.

### 2.4 Los dos trade-offs que definieron el diseño

**Responsividad contra consistencia inmediata.** Se eligió responder al usuario en cuanto la transacción confirma, aceptando que los avisos lleguen segundos después. El coste es que las notificaciones son eventualmente consistentes; la ganancia es que publicar no depende de tres servicios externos (ADR-0005).

**Costo contra disponibilidad.** El presupuesto obliga a una sola máquina sin redundancia. Se eligió una máquina virtual propia en lugar de una plataforma gratuita de aplicaciones, porque esas suspenden el servicio por inactividad y tardan hasta un minuto en despertar: inaceptable durante una sustentación (ADR-0016). El riesgo residual, caída total si cae la máquina, se declara. **Revisión del 2026-11-11:** no se consiguió capa gratuita permanente —Oracle rechazó la creación de la cuenta tras verificar la tarjeta— y la plataforma corre sobre crédito de estudiante de Azure, que es finito. La consecuencia operativa es que la máquina hay que desasignarla cuando no se usa, de modo que **la disponibilidad pasó a depender de una acción manual**, que es peor de lo que esta decisión suponía.

---

## 3. Tácticas aplicadas

Tácticas de Bass, Clements y Kazman, agrupadas por el atributo que sirven. Cada una está justificada en el ADR que la introduce.

| Atributo | Tácticas | Dónde |
|---|---|---|
| **Rendimiento** | Reducir la demanda computacional; mantener múltiples copias de los datos; introducir concurrencia; limitar el tamaño de las colas | ADR-0010: una consulta por pantalla, contadores materializados, caché por evento, paginación acotada |
| **Confiabilidad** | Mantener la consistencia de los datos; encolar; reintentar; controlar la concurrencia; detectar fallos | ADR-0005: una transacción, bandeja de eventos, espera creciente, token de versión |
| **Disponibilidad** | Degradación elegante; mantener múltiples copias de la computación; detectar fallos | ADR-0006 y ADR-0009: canales independientes, SSE y Web Push cubriendo el mismo requisito |
| **Seguridad** | Autenticar actores; autorizar actores; limitar el acceso; limitar la exposición; auditar; validar la entrada; revocar el acceso | ADR-0004, ADR-0012, ADR-0013, ADR-0018 |
| **Mantenibilidad** | Reducir acoplamiento; aumentar cohesión; localizar cambios; usar un intermediario; abstraer servicios comunes | ADR-0001 y ADR-0006 |
| **Verificabilidad** | Verificar y validar; detectar fallos pronto; gestionar la configuración | ADR-0017: puertas bloqueantes en cada cambio |
| **Observabilidad** | Monitorear; detectar fallos; auditar | ADR-0013 y ADR-0014 |

---

## 4. Diseño de alto nivel

Diagramas y explicación completa en [diagramas/01-diseno-alto-nivel-c4.md](diagramas/01-diseno-alto-nivel-c4.md).

### 4.1 Nivel 1 — Contexto

Un sistema, tres grupos de usuarios que son el mismo tipo con distinto rol, y cuatro sistemas externos: el proveedor de identidad y los tres canales de aviso.

Lo que se lee: no hay microservicios; la plataforma no custodia contraseñas; los tres canales son externos, lentos y falibles, lo que obliga a desacoplarlos de la transacción; y el aviso llega **fuera** de la sesión del usuario, porque el destinatario puede no estar conectado.

### 4.2 Nivel 2 — Contenedores

Siete contenedores: cliente web (PWA), proxy inverso, API, proceso de notificaciones, base de datos, almacenamiento de objetos y recolector de observabilidad.

Dos decisiones del nivel 2 que hay que poder defender:

- **El proceso de notificaciones vive dentro del contenedor de la API.** Es un servicio en segundo plano del mismo artefacto, que es lo que corresponde a un monolito modular. Separarlo exigiría otra imagen, otra unidad que desplegar y otra que vigilar, sin driver que lo pida. El `SKIP LOCKED` de la bandeja deja la puerta abierta.
- **Los adjuntos pasan por la API, con la sesión de quien sube o descarga.** El almacenamiento solo es alcanzable por la red interna. Se descartó la URL prefirmada tras fallar con un 403 en el primer uso real (ADR-0012, revisión del 2026-11-13); el coste son hasta 5 MiB por archivo que cruzan la aplicación.

### 4.3 Arquetipo

**Monolito modular con Clean Architecture**, cuatro capas y dependencias dirigidas al dominio. El razonamiento y las alternativas descartadas están en ADR-0001.

---

## 5. Architecture y Solution Building Blocks

Catálogo completo con la evaluación por costo, complejidad, escalabilidad, riesgos, mantenibilidad y evolución en [abb-sbb.md](abb-sbb.md).

### 5.1 Resumen

**17 ABB**, agrupados en cuatro de negocio (reconocimientos, interacción, moderación, estructura académica), cinco transversales (identidad, autenticación, autorización, notificaciones, mensajería) y ocho de plataforma (persistencia, documental, caché, auditoría, observabilidad, configuración, borde, DevSecOps).

Cada ABB se justifica por el driver o requisito que lo obliga. Un ABB sin driver que lo exija no debería existir.

### 5.2 El criterio que decidió los SBB

En **cinco** ABB el SBB «correcto en producción» se descartó por costo o plazo: mensajería (sin broker), documental (autoalojado en vez de gestionado), caché (en memoria en vez de distribuida), configuración (archivo de entorno en vez de gestor de secretos) y borde (proxy en vez de WAF).

En los cinco la decisión fue la misma: **elegir una abstracción estándar que deje la puerta abierta**. La bandeja está tras un puerto, el almacenamiento habla la API de S3, la caché está tras un puerto, la configuración entra por variables de entorno. Eso es lo que hace que sean decisiones de arquitectura y no atajos: la contrapartida está identificada, el punto de cambio nombrado y el costo de cambiar es conocido.

---

## 6. Diseño detallado

### 6.1 Componentes

Detalle en [diagramas/02-diagrama-de-componentes.md](diagramas/02-diagrama-de-componentes.md).

Nivel 3 del modelo C4: qué hay dentro de la API, con las dos clases de flecha que son el corazón de Clean Architecture. Las de «usa» van hacia dentro; las de «implementa» van también hacia dentro y son las que invierten la dependencia. **Ninguna flecha sale del dominio.**

El componente que más conviene explicar es `JerarquiaInstitucional`: un servicio de dominio que responde «¿quién aprueba a quién?» y «¿sobre quién manda este usuario?». Está en el dominio porque es lo más característico del enunciado y lo que más vale poder probar sin infraestructura.

### 6.2 Paquetes

Detalle en [diagramas/03-diagrama-de-paquetes.md](diagramas/03-diagrama-de-paquetes.md).

Cuatro proyectos, uno por capa, de modo que **el compilador imponga** la dirección de las dependencias. Dentro de cada capa, la organización es por módulo primero y por tipo de pieza después:

```text
Kudos/
├── CasosUso/
├── Entrada/{Puertos, Adaptadores, Dto}
└── Salida/Puertos
```

Los puertos de salida viven **en el módulo que los necesita**, no en una carpeta común: así se ve de un vistazo de qué depende cada módulo.

### 6.3 Secuencias

Detalle en [diagramas/04-diagramas-de-secuencia.md](diagramas/04-diagramas-de-secuencia.md). Siete diagramas: la publicación de un Kudo en dos mitades, el fallo de un canal, la consulta del muro, la carga de un adjunto, la aprobación de una cuenta y el veto en cascada.

**La observación más importante del conjunto:** los cinco pasos que exige el enunciado **no ocurren todos en la misma secuencia**. En la transacción ocurren el paso 1 (persistencia) y el paso 5 (auditoría); los pasos 2, 3 y 4 ocurren en el proceso en segundo plano, segundos después. Esa separación es deliberada y es lo que hay que sustentar.

### 6.4 Modelo de datos

Detalle en [diagramas/modelo-de-datos.md](diagramas/modelo-de-datos.md). Tres diagramas entidad-relación: identidad y estructura académica, Kudos e interacción, y notificaciones y auditoría.

Las decisiones de modelado que conviene poder explicar:

- **Las invariantes de RF-04 las defiende la base**, con índices únicos parciales, no solo la aplicación: una validación en la aplicación no resiste dos peticiones simultáneas.
- **La ruta materializada** de las réplicas hace que leer un hilo de cualquier profundidad sea una consulta y que el orden por ruta ya sea el orden de presentación.
- **Los contadores materializados** convierten la agregación más costosa del muro en la lectura de una columna.
- **La tabla de auditoría es de solo inserción por disparador**, con cadena de hashes: ni la propia aplicación puede alterarla.

### 6.5 Despliegue

Detalle en [diagramas/05-diagrama-de-despliegue.md](diagramas/05-diagrama-de-despliegue.md). Tres diagramas: producción, entorno local y el camino del commit al servidor.

---

## 7. Clean Architecture

El enunciado pide explicar capas, dependencias, responsabilidades y reglas arquitectónicas. Razonamiento completo en [ADR-0001](adr/adr-0001-monolito-modular-clean-architecture.md).

### 7.1 Capas

```text
Interfaces (API HTTP)  ──┐
Infraestructura ─────────┤──→ Aplicación ──→ Dominio
                         │
   (adaptadores de salida)
```

| Capa | Contiene | Depende de |
|---|---|---|
| **Dominio** | Entidades con invariantes validadas en el constructor, objetos de valor, servicios de dominio, puertos de repositorio | Nada |
| **Aplicación** | Casos de uso, puertos de entrada y de salida, gateway de notificaciones, catálogo de mensajes | Dominio |
| **Infraestructura** | Adaptadores de salida: persistencia, canales, objetos, caché, instrumentación | Aplicación |
| **Interfaces** | Endpoints, manejador de excepciones, canal SSE, raíz de composición | Aplicación (y la infraestructura solo en la raíz de composición) |

### 7.2 Las cinco reglas

1. El dominio no referencia ninguna otra capa ni ninguna biblioteca de infraestructura.
2. La aplicación depende solo del dominio.
3. La infraestructura implementa puertos de salida y no conoce la capa de interfaces.
4. La API solo conoce puertos de entrada y DTO; nunca entidades ni adaptadores.
5. Un módulo no referencia la implementación de un caso de uso de otro módulo, solo su puerto de entrada.

### 7.3 Quién hace cumplir cada regla

Esta es la tabla que distingue una arquitectura declarada de una verificada:

| Regla | Quién la garantiza |
|---|---|
| 1, 2, 3 | **El compilador**, porque cada capa es un proyecto y las referencias van en un sentido. Las pruebas las guardan para que nadie añada una referencia en sentido contrario |
| **4** | **Solo la prueba de arquitectura.** La API referencia la infraestructura para su raíz de composición, así que el compilador lo permite |
| 5 | **Hoy nadie la hace cumplir automáticamente.** La prueba de arquitectura la cubriría, pero no está escrita: hay cinco módulos (Kudos, Identidad, Interacción, Moderación y Notificaciones) y la regla se sostiene por convención y por revisión. Declararlo es más defendible que decir que está verificada |

**La regla 4 se verificó provocando a propósito una violación:** se añadió un uso de la infraestructura fuera de la raíz de composición, falló solo esa prueba y volvió a verde al retirarlo. Una regla que nunca ha fallado no está verificada.

---

## 8. Manifiesto de Sistemas Reactivos

Análisis completo en [manifiesto-reactivo.md](manifiesto-reactivo.md).

El manifiesto no es una lista de cuatro rasgos independientes: la orientación a mensajes es el cimiento, la resiliencia y la elasticidad son los medios, y la responsividad es el objetivo. En esta plataforma esa cadena se materializa en un punto concreto, **la bandeja de eventos de ADR-0005**, que es por lo que aparece en los cuatro.

| Rasgo | Evaluación | Mecanismo | Limitación declarada |
|---|---|---|---|
| **Orientación a mensajes** | Abordado | Evento depositado en la misma transacción; frontera asíncrona con bajo acoplamiento, aislamiento y transparencia de ubicación | Sin broker: la cola es una tabla, con el punto de cambio identificado |
| **Resiliencia** | Abordado en contención, aislamiento y delegación | Clasificación de errores transitorios y permanentes, reintento con espera creciente, aviso persistido, métricas de la bandeja | **Sin replicación**: una sola máquina |
| **Elasticidad** | Habilitado por el diseño, no ejercido por el despliegue | Sin estado de sesión, `SKIP LOCKED`, caché tras un puerto | Sin escalado; caché y SSE en memoria del proceso |
| **Responsividad** | Abordado, con objetivos medibles | Frontera asíncrona, lado de lectura propio, traza propagada, errores con identificador | Avisos eventualmente consistentes |

**Conclusión honesta:** la plataforma aplica el manifiesto en lo que depende del diseño y queda corta en lo que depende de la infraestructura, por el presupuesto cero. Las dos limitaciones reales, sin replicación y sin escalado, vienen de desplegar en una sola máquina, y en ninguno de los dos casos el diseño es lo que lo impide.

---

## 9. The Twelve-Factor App

Matriz completa, factor por factor analizado, evaluado y justificado, en [twelve-factor.md](twelve-factor.md).

| # | Factor | Estado |
|---|---|---|
| I | Código base | Cumple |
| II | Dependencias | Cumple |
| III | Configuración | Cumple |
| IV | Servicios de respaldo | Cumple |
| V | Construir, publicar, ejecutar | Cumple |
| VI | Procesos | **Cumple parcialmente** |
| VII | Asignación de puertos | Cumple |
| VIII | Concurrencia | **Cumple parcialmente** |
| IX | Desechabilidad | Cumple |
| X | Paridad de entornos | Cumple |
| XI | Historiales | Cumple |
| XII | Procesos de administración | Cumple |

**Los dos parciales son el mismo problema visto desde dos lados:** se despliega una sola instancia en una sola máquina, así que el estado en memoria no molesta (VI) y el escalado horizontal no se ejerce (VIII). Un documento donde los doce factores salen «cumple» suele significar que no se evaluó nada.

Tres factores donde conviene detenerse al sustentar:

- **III Configuración:** lo secreto **no tiene valor por omisión**. Si falta, la aplicación no arranca. Un valor por omisión para un secreto es una contraseña en el repositorio con otro nombre.
- **IX Desechabilidad:** es donde el factor se cruza con la funcionalidad obligatoria. El evento se marca procesado dentro de la misma transacción en que se atiende, así que matar el proceso en el peor momento no pierde ninguna notificación.
- **X Paridad:** las pruebas de integración corren contra PostgreSQL y almacenamiento reales, y las pruebas se ejecutan en un contenedor Linux con **el mismo script que invoca el CI**.

---

## 10. Patrones, SOLID y Clean Code

Desarrollo completo en [patrones-y-clean-code.md](patrones-y-clean-code.md).

### 10.1 Patrones

El enunciado dice que no se evalúa la cantidad, así que la lista va con el problema que cada uno resuelve. **El patrón central es Transactional Outbox**: es el que convierte «publicar un Kudo con cinco efectos» de un problema difícil en uno resuelto.

| Patrón | Problema que resuelve |
|---|---|
| Repository | El dominio necesita persistir sin conocer el ORM |
| Unit of Work | Kudo, auditoría y evento, todo o nada |
| **Transactional Outbox** | Los efectos externos no caben dentro de la transacción ni después de ella |
| Adapter | Cuatro proveedores externos que no pueden filtrarse al dominio |
| Ports and Adapters | Mantener el dominio independiente, de forma verificable |
| Strategy | Cada tipo de aviso usa canales distintos |
| Mediator | Las reglas de cada proveedor no deben repetirse en cada emisor |
| Cache-Aside | Carga predominantemente de lectura |
| Materialized View | Contadores sin agregar en cada lectura |
| Materialized Path | Árbol de réplicas sin límite, leído completo con frecuencia |

**Y ocho descartados**, que dicen más del criterio: CQRS completo con base separada, Event Sourcing, Saga, Specification componible, Decorator para reintentos, Singleton, Factory abstracta y **Valet Key**. Este último merece contarse: era el patrón elegido para los adjuntos (URL prefirmada, los bytes sin pasar por la API) y se **implementó y se abandonó** el 2026-11-13 tras fallar con un 403 en el primer uso real, porque el servidor de objetos valida las firmas con un secreto fijo y público. Hoy los archivos pasan por la API con la sesión de quien actúa (ADR-0012, revisión del 2026-11-13).

### 10.2 SOLID

Cada principio con el mecanismo que lo sostiene y lo que pasaría sin él. El más estructural es **inversión de dependencias**: las interfaces se declaran donde se usan, no donde se implementan, y eso es lo que las pruebas de arquitectura verifican.

### 10.3 Clean Code verificado, no afirmado

El código está en español, como el dominio. Lo que lo hace cumplir no es la buena voluntad sino el pipeline: advertencias como errores, formato verificado, reglas de nomenclatura como error, directivas sin usar como error, espacio de nombres según la carpeta, campos de solo lectura por omisión y, sobre todo, **pruebas de arquitectura**. «Bajo acoplamiento» es una afirmación que cualquier documento puede hacer; una prueba que falla cuando el dominio referencia infraestructura es evidencia.

---

## 11. Desarrollo seguro

Análisis completo, con matriz de 15 riesgos y mapeo al OWASP Top 10, en [desarrollo-seguro.md](desarrollo-seguro.md).

| Punto que pide el enunciado | Decisión central |
|---|---|
| **Autenticación** | Delegada a un proveedor OIDC: la plataforma no custodia contraseñas. El estado de la cuenta se comprueba **en cada petición**, sin caché, así que una desactivación surte efecto de inmediato |
| **Autorización** | En el dominio, no en el token. El rol y el ámbito se resuelven contra la base en cada petición, porque un rol en un claim no se puede revocar antes de que expire y el ámbito por facultad y programa no cabe en un token |
| **Gestión de secretos** | Fuera del repositorio, por variables de entorno **sin valor por omisión**, con doble escaneo. El que importa es el local: detectar un secreto en el pipeline significa que ya está en el remoto |
| **Validaciones** | Todo en el servidor; lo del cliente es comodidad. Las invariantes se validan en el constructor, de modo que no exista una instancia inválida |
| **APIs** | HTTPS obligatorio, token por grupo de rutas y no por política global, errores sin filtración, contrato OpenAPI, **sin webhook entrante** |
| **Integraciones con terceros** | Cuatro, todas tras un puerto. Ninguna recibe un correo o un teléfono desde un módulo de negocio: el gateway resuelve el destinatario por identificador interno |
| **Riesgos** | Matriz de 15, con probabilidad, impacto, mitigación y residual |

**Los dos riesgos residuales más altos, declarados:** la pérdida total por caída de la única máquina (**alto**, viene del presupuesto cero) y la escalada de privilegios por un hueco de la jerarquía (**medio**, porque es código propio de seguridad; se asumió a cambio de tener la jerarquía en el dominio).

---

## 12. Estrategia de pruebas

Detalle completo, con la trazabilidad de cada escenario de calidad a su prueba, en [piramide-de-pruebas.md](piramide-de-pruebas.md).

**Cuatro niveles**, uno más de los tres que exige el enunciado:

| Nivel | Qué responde | Contra qué |
|---|---|---|
| **Unitarias** | ¿La regla de negocio es correcta? | Nada externo |
| **Arquitectura** | ¿Sigue siendo Clean Architecture? | Los ensamblados compilados |
| **Integración** | ¿Funciona contra la infraestructura? | PostgreSQL y almacenamiento **reales**, en contenedores desechables |
| **Punta a punta** | ¿Funciona de verdad? | La API completa levantada y atendida **por HTTP**, contra PostgreSQL real y los tres canales simulados. **No hay un navegador de por medio**: ver la nota de la sección 16.5 |

**Por qué contra infraestructura real y no contra dobles:** una base en memoria no reproduce un índice único parcial, ni el comportamiento de `SKIP LOCKED`, ni el comportamiento de un servidor de objetos real al guardar y leer, ni el truncamiento de precisión que haría fallar la verificación de la cadena de hashes. Es justo lo que hay que verificar.

**Las pruebas negativas son las que importan** en seguridad: la prueba de los adjuntos no es que la descarga funcione, sino que **falle** sin identidad, sin cuenta, para quien no es el autor y con un contenido que no es del tipo declarado.

**Dos verificaciones fuera de la pirámide**, sin las cuales dos escenarios quedarían sin evidencia. La **restauración del respaldo** de la base y del volumen de objetos **está hecha y comprobada** (2026-11-12, sobre una base aparte, con los recuentos cuadrando). La **prueba de carga con percentiles** (QA-REND-01 y QA-REND-02) **no se hizo**, y los umbrales siguen siendo objetivos de diseño y no resultados medidos (ver 16.5).

---

## 13. DevSecOps y despliegue

Razonamiento en [ADR-0017](adr/adr-0017-devsecops-quality-gates.md) y [ADR-0016](adr/adr-0016-despliegue-contenedores-maquina-unica.md); diagramas en [diagramas/05-diagrama-de-despliegue.md](diagramas/05-diagrama-de-despliegue.md).

### 13.1 Las puertas del pipeline, y las dos que no bloquean

Bloquean la construcción: compilación sin advertencias, formato, pruebas unitarias, de arquitectura, de integración y de punta a punta, tipos, lint, pruebas y presupuesto de peso del cliente, escaneo de secretos (en los dos repositorios), Trivy sobre las dependencias y la configuración, y Trivy sobre la imagen, que se publica solo si pasa.

**Dos no bloquean, y hay que decirlo:** el umbral de cobertura del núcleo (está en 0 %) y la puerta de calidad de Sonar, cuyo análisis se envía sin esperar el veredicto. La de Sonar sí se **evalúa** y se consulta: falló dos veces el 2026-11-13 y se resolvió con pruebas, no con el umbral (ver 16.5). Hace falta decirlo porque una versión anterior de este documento hablaba de «doce puertas, todas bloqueantes» e incluía un «contrato al día» que no existe como puerta.

**Una puerta que solo informa es documentación, no una puerta.** Convertir esas dos en bloqueantes es una línea de configuración cada una; lo que costó fue tener la cobertura que lo permitiera.

### 13.2 Cuatro criterios de diseño del pipeline

1. **Bloquean las vulnerabilidades altas y críticas con corrección disponible.** Bloquear por algo sin parche detiene el trabajo sin ofrecer salida, y así se acaba desactivando la puerta.
2. **Las acciones externas van fijadas por identificador de commit**, no por etiqueta: una etiqueta se puede mover a código distinto.
3. **Se escanea la imagen y se publica exactamente la imagen escaneada**, sin reconstruirla: reconstruir invalida el escaneo.
4. **El escaneo de secretos corre dos veces, y el que importa es el local.**

### 13.3 Despliegue

Una máquina virtual propia, con todo en contenedores y un único puerto expuesto tras un proxy con TLS automático. HTTPS no es una mejora: el service worker de la PWA y la API de notificaciones solo funcionan sobre un origen seguro.

El criterio que decidió entre las opciones no fue técnico sino de riesgo de calendario: **que la plataforma responda de inmediato cuando el docente abra la URL**.

---

## 14. Observabilidad y auditoría

Razonamiento en [ADR-0014](adr/adr-0014-observabilidad-opentelemetry.md) y [ADR-0013](adr/adr-0013-auditoria-cadena-hashes.md).

### 14.1 Son dos cosas distintas

| | Logs, trazas y métricas | Auditoría |
|---|---|---|
| **Para qué** | Diagnosticar | Rendir cuentas |
| **Público** | Quien opera el sistema | La institución |
| **Vida** | Efímeros, se rotan y se pierden | Permanente e inalterable |
| **Dónde** | Salida estándar y recolector OTLP | Tabla de solo inserción en PostgreSQL |

El enunciado las menciona juntas y conviene separarlas: **«está en los logs» no es una auditoría.**

### 14.2 La decisión central de observabilidad

**La traza cruza la frontera asíncrona.** Al escribir un evento en la bandeja se guarda el contexto de traza de la petición que lo originó; al procesarlo, el proceso en segundo plano lo retoma y continúa esa misma traza.

Sin eso, la publicación y sus tres avisos serían trazas inconexas y no se podría responder «¿qué le pasó a **esta** publicación?», que es QA-OBS-01. La verificación es concreta: publicar un Kudo, tomar el identificador de traza de la respuesta y encontrar en una sola vista la petición, la transacción, el evento y los tres envíos.

### 14.3 La decisión central de auditoría

**Cadena de hashes sobre una tabla de solo inserción**, garantizada por un disparador que ni la propia aplicación puede saltarse. Eso convierte la inmutabilidad en algo **demostrable**: la operación de verificación recorre la cadena y detecta cualquier alteración. Está expuesta como consulta de la plataforma, para poder ejecutarla en vivo durante la sustentación.

**Qué se audita, además de lo obvio:** los accesos **rechazados** por falta de permiso. Un intento fallido suele informar más que uno exitoso.

**Qué no:** datos personales innecesarios. Al cambiar un correo se registra qué campos cambiaron, no los valores. La auditoría es permanente, así que meter datos personales de más crea un problema perpetuo.

### 14.4 Las métricas que importan aquí

Además de las técnicas habituales: **eventos pendientes en la bandeja, antigüedad del más viejo, envíos fallidos por canal y eventos descartados**. Una bandeja que crece es el primer síntoma de que un proveedor dejó de responder, antes de que alguien se queje.

---

## 15. Decisiones de arquitectura

Los 19 ADR están en [adr/README.md](adr/README.md), todos con la misma estructura: metadatos, contexto, decisión, alternativas comparadas por atributo de calidad, consecuencias positivas y negativas, tácticas de Bass, escenarios y requisitos relacionados, ADR relacionados, notas de implementación y referencias.

| ADR | Decisión |
|---|---|
| 0001 | Monolito modular con Clean Architecture y reglas verificadas |
| 0002 | C# sobre .NET 10, reutilizando infraestructura propia ya probada |
| 0003 | PostgreSQL con mapeo explícito y migraciones versionadas |
| 0004 | Identidad delegada por OIDC; autorización por jerarquía propia |
| 0005 | Transactional Outbox y concurrencia optimista |
| 0006 | Gateway de notificaciones multicanal |
| 0007 | WhatsApp con Meta Cloud API, solo salida y por plantilla |
| 0008 | Correo por SMTP con proveedor sustituible |
| 0009 | SSE para conectados y Web Push para desconectados |
| 0010 | CQRS ligero, contadores materializados, caché por evento, cursor |
| 0011 | Árbol de réplicas con padre y ruta materializada |
| 0012 | Adjuntos en objetos privados, servidos por la API |
| 0013 | Auditoría de solo inserción con cadena de hashes |
| 0014 | OpenTelemetry con la traza propagada a la frontera asíncrona |
| 0015 | Cliente como PWA mobile first en React |
| 0016 | Contenedores sobre una máquina virtual propia (revisado: no se consiguió capa gratuita permanente) |
| 0017 | DevSecOps con puertas bloqueantes y pirámide automatizada |
| 0018 | Configuración por entorno y secretos fuera del repositorio |
| 0019 | Alcance de la línea base en tres niveles declarados |

### 15.1 Una revisión que conviene no esconder

**ADR-0002 fue revisado el mismo día que se escribió.** La primera versión elegía Java con Spring Boot por ser el estilo de referencia del curso. Se revisó al inventariar la infraestructura propia ya construida y probada por el autor, que resuelve el núcleo de la funcionalidad obligatoria.

El ADR conserva el razonamiento completo y las dos alternativas en lugar de presentarse como si hubiera nacido acertado. Un ADR que registra su propia revisión es más defendible que uno que la borra.

### 15.2 Decisiones conscientemente aplazadas

Quedan registradas para que no se confundan con omisiones: sin broker de mensajes, sin caché distribuida, sin réplica de lectura, número de prueba de WhatsApp en lugar de número propio, sin gestor de secretos dedicado y sin almacenamiento gestionado. Cada una con su punto de revisión nombrado.

---

## 16. Alcance y estado

Declarado en [ADR-0019](adr/adr-0019-alcance-linea-base.md), en tres niveles.

### 16.1 Nivel 1 — La línea base

Publicar Kudo con sus cinco pasos, autenticación, los siete roles con exclusividad y unicidad, categorías con administración restringida, adjuntos, muro paginado, estructura académica mínima, observabilidad, pipelines con Sonar y Trivy, y los cuatro niveles de pruebas.

### 16.2 Nivel 2 — Con margen, en este orden

**Completo y con pantallas** desde el 2026-11-13: flujo de aprobaciones, réplicas anidadas, calificaciones, y reportar y vetar contenido. Cada pieza se verificó en producción.

### 16.3 Nivel 3 — Diseñado y documentado, no implementado

Gestión completa de la estructura académica, reportes e indicadores, número propio de WhatsApp y la **entrega por WhatsApp** del código de verificación del celular. El código en sí **sí se implementó** (de un solo uso, con vigencia e intentos limitados), pero se entrega por correo; ver 16.5.

### 16.4 Reglas de gobierno

1. Nada del nivel 2 empieza antes de que el nivel 1 esté desplegado y con sus evidencias recogidas.
2. **Lo no implementado se declara**, aquí y en la sustentación.
3. El modelo de datos cubre los tres niveles desde el principio: prever una tabla es barato, migrar con prisa el último día no lo es.
4. Una funcionalidad incompleta no se despliega: es peor que ausente.
5. El último día es para revisión, no para desarrollo.

### 16.5 Estado

**Revisión del 2026-11-11.** La versión anterior de esta tabla describía el cierre de la Fase 0 y
declaraba la funcionalidad sin implementar y el despliegue sin hacer. Se deja constancia del cambio
en lugar de sustituirlo en silencio: la regla 2 de gobierno obliga a declarar lo no implementado, y
eso vale en los dos sentidos.

| Elemento | Estado |
|---|---|
| Documentación | 19 ADR, drivers, este DAS, 7 documentos de análisis, 18 diagramas Mermaid, manual de puesta a punto |
| Estructura del código | Cuatro capas en cuatro proyectos, compilando sin advertencias |
| Pruebas | **199 unitarias, 6 de arquitectura, 59 de integración y 93 de punta a punta** contra PostgreSQL real y un almacenamiento de objetos real; **137 del cliente web**. Todas en verde en el pipeline |
| Cobertura | **88,1 % sobre código nuevo** según Sonar (2026-11-13), por encima del 80 % exigido; la cifra global sigue siendo baja porque cuenta infraestructura y API, que solo prueban la integración y los escenarios de punta a punta. Ninguna de las dos puertas detiene hoy el pipeline: ver la nota de abajo |
| Pipeline | **Ejecutándose en GitHub Actions**, 6 trabajos en el back-end y 4 en el cliente, todos bloqueantes |
| Despliegue | **Hecho.** <https://kudos.20.83.117.236.sslip.io>, con HTTPS y certificado de una autoridad real |
| Funcionalidad | **Niveles 1 y 2 implementados y desplegados.** Los cinco efectos de publicar un Kudo funcionan contra el sistema desplegado (persistencia, correo, WhatsApp, tiempo real y auditoría), con adjuntos, categorías, registro y aprobación de cuentas, réplicas anidadas, calificaciones, y reportar y vetar. Del nivel 3 solo está el código de verificación, con la limitación declarada abajo |
| Canales | **Los tres reales en producción** desde el 2026-11-12: correo por SMTP, WhatsApp por Meta Cloud API y tiempo real por SSE y Web Push. El correo sale por Gmail con contraseña de aplicación, no por un proveedor transaccional: ver la nota de abajo |

**Lo que no está, y conviene decirlo antes de que se pregunte:**

- El **proveedor transaccional de correo**. El canal real funciona desde el 2026-11-12, pero sale
  por SMTP de Gmail con una contraseña de aplicación, no por un proveedor transaccional: se
  intentó con Brevo y su verificación por SMS nunca llegó. La consecuencia hay que decirla: no hay
  métricas de entrega, ni gestión de rebotes, ni reputación de dominio propio, y el límite diario
  es bajo. El camino que recorre el correo es el mismo; lo que falta es la operación alrededor.
- **Registro, verificación y aprobación de cuentas (RF-01 a RF-03):** implementados de punta a punta
  el 2026-11-13 (pantallas de registro, de verificación y de solicitudes pendientes; la API y las
  pruebas de punta a punta recorren el flujo completo y la regla de que la cuenta solo se activa con
  solicitud aprobada + correo verificado + celular verificado, en cualquier orden). Limitaciones que
  hay que decir: (1) **el código del celular llega por correo, no por WhatsApp**, porque Meta no
  permite plantillas de autenticación sin verificación comercial (ADR-0006), así que el celular se
  verifica solo en el sentido de que quien se registra recibe un código, no de que posea ese
  número; la verificación real por WhatsApp sigue como nivel 3; (2) el rol solicitado lo aprueba la
  jerarquía del enunciado, pero el primer Administrador y las cuentas de demostración se crean
  fuera de la aplicación; (3) para entrar, además de estar activa en la plataforma, la persona
  necesita una identidad en el proveedor (Auth0), que hoy se crea aparte. Consecuencia que conviene
  saber explicar: **autenticarse en el proveedor no basta para entrar**; quien no tenga persona
  activa en la plataforma recibe «No existe una cuenta» (ADR-0004).
- Del cliente web falta el **detalle de un Kudo** como pantalla propia.
- Los **adjuntos** (RF-08) están implementados: se suben desde el formulario de publicar y se abren
  desde el muro, con escenarios de punta a punta del flujo completo (reservar, subir, confirmar, descargar). **Verificados en
  producción desde un celular (2026-11-13): subida y apertura de una imagen.** El primer intento real falló con un 409 por un
  defecto de mapeo de EF Core y el segundo con un 403 del almacenamiento, ambos invisibles a las pruebas; el segundo se resolvió rediseñando la ruta (archivos por la API). Detalle en
  `docs/evidencias/00-registro-de-verificaciones.md`.
- No hay **prueba de carga**, así que los umbrales de QA-REND-01 y QA-REND-02 están sin medir.
  Son objetivos de diseño, no resultados verificados.
- Las pruebas de **punta a punta son por HTTP, no dirigidas desde un navegador**. Un escenario de
  interfaz exigiría credenciales reales del proveedor de identidad en el pipeline; la interfaz se
  cubre con pruebas de componentes y con las capturas de `docs/evidencias`.
- **La exportación de telemetría estuvo sin implementar hasta el 2026-11-11**, y conviene dejarlo
  escrito porque es el fallo más instructivo de la entrega. La instrumentación propia existía —un
  `ActivitySource` y los contadores de negocio—, el panel estaba desplegado y sano, y la variable
  `OTEL_EXPORTER_OTLP_ENDPOINT` apuntaba a él. **Todo parecía configurado.** Pero no había ningún
  paquete de OpenTelemetry en la solución, así que no existía nadie que recogiera esas señales y
  las enviara: la instrumentación emitía al vacío y el panel mostraba cero trazas y cero registros
  sin que nada fallara. Se detectó al ir a recoger la evidencia, no antes, porque **ningún síntoma
  apunta a una telemetría que no se exporta**: la aplicación funciona igual de bien.
- **El nivel 2 está completo y con pantallas** (2026-11-13): el flujo de aprobaciones, reportar y
  vetar, calificar con cinco estrellas y las **respuestas anidadas** (hilo con sangría, responder a
  una respuesta, y calificar, reportar y vetar cada una). Aprobaciones y moderación están
  verificadas en producción; calificar y responder se verificaron en producción cuando se
  desplegaron (ver el registro de verificaciones).
  Reportar y vetar tienen 20 escenarios de punta a punta: solo el Rector y el Coordinador
  Académico vetan, el muro, el hilo y el detalle dejan de servir lo vetado, la cascada oculta al
  Kudo y su hilo o a una réplica y sus descendientes sin tocar a las hermanas, el autor recibe el
  motivo, vetar resuelve los reportes pendientes y todo queda auditado. Esas pruebas encontraron un
  defecto: el resultado de vetar un Kudo con hilo decía «1 contenido afectado» en vez del total,
  porque contaba las réplicas después de marcarlas.

**Sobre la cobertura, que necesita tres cifras y no una.** Una versión anterior de esta sección
decía que la cobertura era del 18,34 % y que la puerta no bloqueaba. Las dos cosas eran ciertas por
separado y juntas daban una idea equivocada, así que conviene separarlas.

| Medida | Valor | ¿Bloquea? |
|---|---|---|
| Umbral de `dotnet test` en el pipeline | **0 %** | No. Está escrito y nunca subió |
| Cobertura global de la solución | **18,34 %** | — Incluye infraestructura y API |
| Puerta de calidad de Sonar sobre **código nuevo** | **88,1 %**, exigido ≥ 80 % (2026-11-13) | Pasa, pero el pipeline no la espera |

El desglose por proyecto, tomado del pipeline, es el que de verdad explica esas cifras:

| Proyecto | Líneas | Ramas |
|---|---|---|
| `UcoKudos.Domain` | **47,24 %** | 44,17 % |
| `UcoKudos.Application` | **0,93 %** | 1,78 % |

**La puerta falló dos veces el 2026-11-13 y se arregló con pruebas, no con el umbral.** Al añadir
el registro, la verificación y la moderación, la cobertura sobre código nuevo cayó a 19,9 %, y tras
las primeras pruebas unitarias subió a 72,4 %: los casos de uso nuevos —y los adjuntos, reescritos en
el mismo periodo— estaban cubiertos por las pruebas de punta a punta, que Sonar no recibe. Se
escribieron pruebas unitarias con dobles de cada caso de uso (199 en total) y la puerta pasó a 88,1 %.
El umbral de 80 % no se tocó.

**Ese 0,93 % no significa que la capa de aplicación esté sin probar, y conviene entender por qué.**
El informe de cobertura que se genera y que se envía a Sonar sale **solo de las pruebas
unitarias**: el pipeline apunta a `tests/UcoKudos.UnitTests/.../coverage.opencover.xml`. Los casos
de uso se ejercitan, además, desde las **59 pruebas de integración y las 93 de punta a punta**, cuya
cobertura no se recoge ni se fusiona con la anterior. (Las cifras 47,24 % y 0,93 % de la tabla son de
antes de añadir las pruebas unitarias de los casos de uso; hoy la capa de aplicación está bastante
por encima, pero esa tabla no se volvió a medir proyecto por proyecto y no se inventa una cifra nueva.) Así que el instrumento mide menos de lo que el proyecto
prueba.

Esa es la limitación real, y es metodológica antes que de esfuerzo: **la cifra que se publica no
representa lo que se ejercita**. Fusionar la cobertura de los tres proyectos de prueba daría un
número honesto; hoy no se hace.

El 88,1 % de Sonar sobre código nuevo es cierto y es la única cifra comparable con el objetivo del
ADR, pero hay que leerla con lo anterior delante: mide código nuevo que, además, resulta estar
cubierto por unitarias. Lo que quedó cubierto por unitarias ya no es solo lo que era fácil de probar.

**Lo que sigue siendo verdad, y es lo que hay que declarar:** ninguna de las dos puertas de
cobertura detiene el pipeline hoy. La de `dotnet test` porque su umbral es 0, y la de Sonar porque
el análisis se envía sin `sonar.qualitygate.wait`, así que el trabajo termina antes de que
SonarCloud evalúe el resultado. La puerta existe, se cumple y se puede ver en el tablero, pero un
incumplimiento futuro no rompería la construcción. Convertirla en bloqueante es una línea de
configuración.

---

## 17. Riesgos

Matriz de seguridad completa en [desarrollo-seguro.md](desarrollo-seguro.md). Los riesgos del proyecto, no solo de seguridad:

| # | Riesgo | Impacto | Mitigación |
|---|---|---|---|
| P-01 | **La plantilla de WhatsApp no se aprueba a tiempo.** El destinatario nunca escribió primero, así que todos los envíos van por plantilla: sin aprobación no sale ningún mensaje | Crítico | Enviarla a aprobación el primer día. Es la tarea con dependencia de un tercero más temprana. **Estado (2026-11-13): materializado en parte y resuelto en parte.** La plantilla del aviso de Kudo se aprobó y el canal opera en producción; la de **autenticación** (el código de verificación) Meta no deja crearla sin verificación comercial, y por eso ese código sale por correo |
| P-02 | La cuenta del proveedor de nube exige verificación y tarda | Alto | Primera tarea de infraestructura, antes que cualquier otra. **Estado: materializado y resuelto con otro proveedor:** Oracle rechazó la cuenta y se desplegó en Azure con crédito de estudiante (ADR-0016, revisión del 2026-11-11) |
| P-03 | El plazo no alcanza para el nivel 1 completo | Alto | ADR-0019: el alcance está priorizado y la regla de gobierno protege la funcionalidad obligatoria. **Estado: no se materializó**; el nivel 1 y el nivel 2 se entregaron |
| P-04 | **Caída total por ser una sola máquina sin redundancia** | Crítico | Respaldo probado de base y objetos. Es recuperación, no disponibilidad: el riesgo residual es **alto** y viene del presupuesto. **Revisión del 2026-11-12: implementado y comprobado.** `scripts/respaldar.sh` vuelca la base con `pg_dump` y empaqueta el volumen de objetos; `scripts/restaurar.sh` restaura **sobre una base aparte** y compara los recuentos con la viva, de modo que la restauración se prueba sin tocar producción ni esperar al día de la catástrofe. Primera comprobación con éxito el 2026-11-12: las cinco tablas cuadran |
| P-05 | El pipeline falla la mañana de la entrega | Alto | Procedimiento manual de despliegue documentado |
| P-06 | La demostración solo funciona en la red de casa | Alto | Probar el flujo completo desde un celular con datos móviles, fuera de esa red, al menos un día antes |
| P-07 | El número de prueba de Meta solo envía a destinatarios registrados | Medio | Registrarlos antes de la demostración, incluido el del docente si va a recibir un Kudo en vivo |
| P-08 | Memoria insuficiente con los contenedores en una sola máquina | Medio | Vigilar. Es la razón por la que ADR-0005 descartó añadir un broker. **Estado:** hay seis contenedores en una máquina de 8 GB (Azure, no gratuita) con el uso de memoria muy por debajo del límite |
| P-09 | Reutilizar infraestructura propia sin comprenderla, lo que fallaría frente a la exigencia de sustentar | Alto | Cada pieza adaptada se acompaña de sus pruebas, que es la forma de demostrar que se entiende lo que hace |

**P-01 es el riesgo más urgente** y el único que no depende del autor: la aprobación de la plantilla la decide Meta.
