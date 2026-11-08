# ADR-0015: cliente web como PWA mobile first en React con TypeScript

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0015 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB de la capa de presentación |

## Contexto

DRV-001 es el driver más cuantificado del enunciado: «se estima que aproximadamente el 90 % de los usuarios utilizará la plataforma desde dispositivos móviles» y «la estrategia de diseño deberá ser Mobile First». RT-02 exige un repositorio de front-end separado.

Mobile First no es «que quepa en el celular». Significa diseñar la pantalla pequeña primero y ampliarla después, con tres consecuencias concretas: el peso inicial del cliente importa porque se descarga en red móvil, los objetivos táctiles deben alcanzarse con el pulgar, y cada petición de red cuesta latencia real.

Además RF-16 (ADR-0009) necesita un *service worker* para Web Push, y la línea base debe demostrar notificaciones llegando a un celular.

El autor tiene experiencia previa con React y TypeScript, y con un conjunto de herramientas de front-end ya configurado: generación de tipos a partir del contrato OpenAPI, pruebas con verificación automática de accesibilidad y una regla de análisis estático que impide publicar secretos en las variables del cliente.

## Decisión

Un **cliente web único, mobile first, construido como PWA**, en **React con TypeScript**.

1. **Una sola aplicación web adaptable**, no una aplicación móvil nativa y otra web. El 10 % que entra desde escritorio usa la misma, ampliada.
2. **PWA**: manifiesto de aplicación, *service worker* e instalable en la pantalla de inicio. Tres razones, en orden de peso: habilita Web Push para RF-16, permite cachear el armazón de la aplicación para que abra rápido en red lenta, y da una experiencia de aplicación sin pasar por una tienda.
3. **Mobile First de forma verificable**, con tres presupuestos declarados y medidos, no con una intención:
   - Peso inicial del cliente por debajo de 300 KB comprimidos (QA-MOV-01).
   - Primera pantalla útil en menos de 2,5 s en red móvil simulada.
   - Objetivos táctiles de al menos 44 píxeles, navegación principal al alcance del pulgar y ninguna pantalla con desplazamiento horizontal.
4. **TypeScript con tipos generados del contrato OpenAPI del back-end.** Los tipos no se escriben a mano: se generan del contrato, y el pipeline falla si el contrato cambió y los tipos no se regeneraron. Así una ruptura de contrato se detecta al compilar y no en producción.
5. **Desplazamiento infinito con paginación por cursor**, que es el consumo natural de ADR-0010.
6. **Autenticación por el flujo de código de autorización con PKCE**, con los tokens en memoria y **nunca** en el almacenamiento local del navegador: un token en almacenamiento local es accesible a cualquier script inyectado.
7. **Accesibilidad verificada automáticamente.** Las pruebas de componentes incluyen una comprobación de accesibilidad que falla si se introduce un control sin nombre accesible o un contraste insuficiente. En una pantalla de celular, al sol, el contraste no es un adorno.
8. **Ninguna decisión de autorización en el cliente.** El cliente oculta lo que el usuario no puede hacer por comodidad, pero la regla la aplica la API (ADR-0004). Un menú escondido no es un control de acceso.

## Alternativas consideradas

| Criterio | **PWA en React + TypeScript (elegida)** | A. Angular | B. Aplicación nativa o React Native | C. Renderizado en el servidor | D. Web adaptable sin PWA |
|---|---|---|---|---|---|
| Habilita Web Push para RF-16 | Cumple — *service worker* | Cumple | Cumple | Parcial | **No cumple** |
| Mobile First (DRV-001) | Cumple | Cumple | Cumple | Cumple | Cumple |
| Esfuerzo y herramientas ya configuradas (RN-02) | Cumple — tipos, pruebas y reglas listas | Parcial — hay que configurarlo | **No cumple** — dos plataformas, firmas, tiendas | Parcial | Cumple |
| Instalable en la pantalla de inicio | Cumple | Cumple | Cumple | No cumple | No cumple |
| Peso inicial (QA-MOV-01) | Cumple — con división de código | Parcial — base mayor | No aplica | Cumple — el mejor en este punto | Cumple |
| Una sola base de código | Cumple | Cumple | No cumple | Cumple | Cumple |
| Publicación (RT-05) | Cumple — archivos estáticos | Cumple | **No cumple** — revisión de tienda, fuera de plazo | Parcial — exige servidor propio | Cumple |

- **A. Angular** — descartada por poco. Es una opción perfectamente válida, su soporte de PWA y de Web Push es excelente y además es el marco del otro proyecto del curso del autor. Se descarta porque las herramientas de front-end que el autor ya tiene configuradas y probadas —generación de tipos del contrato, pruebas con accesibilidad, regla contra secretos— son para React, y en cinco días reconfigurarlas no aporta nada al resultado evaluado.
- **B. Aplicación nativa o multiplataforma** — descartada: el 90 % móvil sugiere lo nativo, pero la publicación en una tienda exige una revisión que no cabe en el plazo y contradice RT-05, que pide una URL pública que el docente pueda usar. Una PWA instalable cubre el beneficio real sin ese costo.
- **C. Renderizado en el servidor** — descartada: daría la mejor primera carga, que es un argumento serio para DRV-001, pero añade un servidor de front-end que desplegar, escanear y mantener, y complica la autenticación y el *service worker*. El presupuesto de peso se alcanza con división de código.
- **D. Web adaptable sin PWA** — descartada: sería más simple, pero sin *service worker* no hay Web Push, y RF-16 se quedaría solo con SSE, que es lo que ADR-0009 ya descartó como insuficiente.

## Consecuencias

**Positivas**

- Una sola base de código cubre el 100 % de los usuarios, con el 90 % móvil como caso de diseño primario.
- El *service worker* habilita las notificaciones que RF-16 exige y mejora la apertura en red lenta.
- Los tipos generados del contrato convierten una ruptura entre front y back en un error de compilación.
- La accesibilidad y el peso se verifican en el pipeline, así que Mobile First es una afirmación medida y no una declaración.
- Se publica como archivos estáticos: sin servidor de front-end que operar.

**Negativas**

- **El *service worker* es la fuente de los errores más difíciles de diagnosticar del front-end**: una versión cacheada que no se actualiza deja al usuario con una aplicación vieja sin que nada parezca fallar. Hay que definir una estrategia de actualización explícita y probarla.
- Mantener los tres presupuestos exige disciplina: una biblioteca de componentes grande rompe el de peso sin avisar.
- El renderizado en el cliente penaliza la primera carga frente a la alternativa C. Se mitiga con división de código y con el armazón cacheado.
- Los tokens en memoria se pierden al recargar la página, lo que obliga a renovar la sesión de forma silenciosa contra el proveedor de identidad.

## Tácticas de Bass aplicadas

- **Reducir la demanda computacional** — división de código y presupuesto de peso.
- **Mantener múltiples copias de los datos** — armazón de la aplicación cacheado en el cliente.
- **Aumentar la cohesión de la interfaz** — una sola base de código adaptable.
- **Validar la entrada** — validación en el cliente por comodidad, siempre repetida en la API.

## Escenarios y requisitos relacionados

- `QA-MOV-01` — publicar desde un celular de gama media, con los tres presupuestos como medida.
- `QA-REND-01` — el cliente consume la lectura paginada por cursor.
- `RF-16` — el *service worker* que Web Push necesita.
- `DRV-001`, `RT-02`, `RT-05`.

## ADRs relacionados

- **ADR-0009** — Web Push y SSE, que este cliente consume.
- **ADR-0010** — la paginación por cursor y los validadores HTTP.
- **ADR-0004** — la autenticación delegada que el cliente inicia.
- **ADR-0017** — el pipeline que mide los presupuestos.

## Notas de implementación

- Los presupuestos se comprueban en el pipeline, no a ojo. Un presupuesto que no falla la construcción no es un presupuesto.
- La estrategia de actualización del *service worker* se decide y se documenta desde el principio: qué se cachea, qué nunca se cachea y cómo se fuerza una actualización. Las llamadas a la API nunca se cachean.
- La suscripción de Web Push se vuelve a comprobar al arrancar la aplicación, porque la actualización del *service worker* puede invalidarla (ADR-0009).
- Se prueba en un celular real antes de la entrega, no solo en el emulador del navegador: el teclado en pantalla, el alcance del pulgar y el contraste al sol no se reproducen en un escritorio.
- La regla de análisis estático que impide poner secretos en las variables del cliente se mantiene activa: todo lo que se compila en el cliente es público (ADR-0018).

## Referencias

- W3C. *Web Application Manifest*, *Service Workers* y *Push API.*
- W3C. *Web Content Accessibility Guidelines (WCAG) 2.1*, nivel AA.
- Marcotte, E. *Responsive Web Design.*
- IETF. *OAuth 2.0 for Browser-Based Apps* y RFC 7636 (PKCE).
