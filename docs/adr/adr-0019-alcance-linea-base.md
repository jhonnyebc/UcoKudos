# ADR-0019: alcance de la línea base — qué se implementa y qué se diseña

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0019 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | Gobierno del alcance |

## Contexto

El enunciado describe una plataforma completa: siete roles, flujo de aprobaciones, gestión académica, categorías, Kudos con adjuntos, réplicas anidadas, calificaciones, reportes y moderación. Y a la vez define con precisión qué es lo mínimo exigible:

> «Como evidencia mínima de la línea base, deberán implementar el flujo completo de: Publicación de Kudo.»

El objetivo declarado es «diseñar e implementar la **línea base arquitectónica**», y el enunciado insiste en que «el objetivo no es únicamente construir software, sino demostrar capacidad para diseñar arquitectura, justificar decisiones técnicas, automatizar calidad y materializar una línea base empresarial real».

El plazo es de cinco días, y en ese plazo caben además el despliegue, las evidencias, la documentación y la sustentación (RN-02).

Hay dos formas de fracasar aquí. Una es implementar poco y no tener línea base. La otra, más probable y más grave, es intentar implementar todo y llegar a la entrega con nueve funcionalidades a medias, sin despliegue, sin evidencias y sin documentación. El enunciado exige que **todo** esté simultáneamente entregado, desplegado, documentado y sustentado.

## Decisión

Declarar el alcance de forma explícita en tres niveles, y documentar los tres. **Lo que no se implementa se diseña, y se dice que no está implementado.**

### Nivel 1 — Implementado y funcionando en la URL pública

Es la línea base. Todo lo que el enunciado exige como mínimo, y lo imprescindible para que ese mínimo tenga sentido.

| Alcance | Por qué entra |
|---|---|
| **Publicar Kudo con sus 5 pasos**: persistir, correo, WhatsApp, push en tiempo real y auditoría | Es la funcionalidad obligatoria (RF-07) |
| Autenticación y sesión | Sin ella no hay autor ni destinatario |
| Los 7 roles con exclusividad y unicidad | RF-04; son las invariantes que sostienen todo lo demás |
| Catálogo de categorías con sus 15 categorías y su administración restringida | RF-06; un Kudo necesita categoría |
| Adjuntos en los Kudos | RF-08 y DRV-004, que el enunciado destaca |
| Muro de Kudos con lectura paginada | RF-13 y DRV-002, el segundo driver más explícito |
| Estructura académica mínima: facultades, programas y su relación con las personas | Es el ámbito de la autorización (ADR-0004) |
| Observabilidad: métricas, trazas, logs y salud | DRV-006 |
| Pipeline de CI y CD con Sonar y Trivy | DRV-007, RT-03, RT-04 |
| Los cuatro niveles de la pirámide de pruebas | RT-06 |

### Nivel 2 — Implementado si el calendario lo permite, en este orden

Se abordan solo con el nivel 1 cerrado y verificado en la URL pública. El orden no es arbitrario: va de lo que más completa la narrativa arquitectónica a lo que menos.

1. **Réplicas anidadas** (RF-09). Primero porque ADR-0011 es una de las decisiones de diseño más interesantes de sustentar, y porque el modelo de datos ya lo contempla.
2. **Calificaciones** (RF-10). Barato: una tabla, un índice único y un contador materializado que ya existe.
3. **Flujo de aprobaciones** (RF-03). El servicio de dominio de la jerarquía ya responde «quién aprueba a quién» en el nivel 1; falta la máquina de estados de la solicitud y sus pantallas.
4. **Reportar y vetar contenido** (RF-11, RF-12). El veto en cascada depende de que existan las réplicas.

### Nivel 3 — Diseñado y documentado, no implementado

Se entrega su diseño en el documento de arquitectura y el modelo de datos lo contempla, con una nota explícita de que no está implementado: verificación del celular por código de un solo uso, gestión completa de la estructura académica, reportes e indicadores, y número propio de WhatsApp verificado en lugar del de prueba.

### Reglas de gobierno del alcance

1. **Nada del nivel 2 empieza antes de que el nivel 1 esté desplegado y verificado** en la URL pública, con sus evidencias recogidas.
2. **Lo no implementado se declara.** En el documento de arquitectura, en el estado de cada requisito y en la sustentación. Presentar como terminado algo que no lo está es el riesgo más alto frente a RN-01, porque la sustentación admite «implementaciones en vivo» y «cambios de código».
3. **El modelo de datos cubre los tres niveles** desde el principio. Es barato prever una tabla y caro migrar el esquema con prisa el último día.
4. **Una funcionalidad incompleta no se despliega.** Es peor que ausente: el docente la encontrará y fallará.
5. **El último día es para revisión, no para desarrollo.**

## Alternativas consideradas

| Criterio | **Alcance en tres niveles declarados (elegida)** | A. Implementar todo el enunciado | B. Solo el mínimo literal | C. Avanzar sin declarar el alcance |
|---|---|---|---|---|
| Línea base completa y desplegada (RT-05) | Cumple | **No cumple** — riesgo alto de no llegar | Cumple | Parcial |
| Documentación y evidencias completas | Cumple | No cumple — se sacrifican al final | Cumple | Parcial |
| Demuestra capacidad de diseño | Cumple — se diseña más de lo que se implementa | Cumple | Parcial — diseño pobre | Parcial |
| Honestidad en la sustentación (RN-01) | Cumple — el alcance está escrito | Parcial | Cumple | **No cumple** — se descubre en vivo |
| Aprovecha el tiempo disponible | Cumple — el nivel 2 absorbe el margen | No cumple | Parcial — deja margen sin usar | Cumple |
| Riesgo de llegar con todo a medias | Bajo | **Alto** | Muy bajo | Alto |

- **A. Implementar todo** — descartada: es la tentación natural y el modo de fracaso más común. El enunciado exige que todo esté simultáneamente documentado, desplegado y sustentado; nueve funcionalidades al 70 % no cumplen nada.
- **B. Solo el mínimo literal** — descartada: cumpliría, pero desperdiciaría el margen y dejaría una línea base más pobre de lo alcanzable. El nivel 2 existe para absorber ese margen de forma ordenada.
- **C. Avanzar sin declarar el alcance** — descartada: es lo peor de las dos. Sin una frontera escrita, la decisión de qué dejar fuera se toma la última noche, con cansancio, y en la sustentación aparecen huecos que el autor no anticipó.

## Consecuencias

**Positivas**

- La funcionalidad obligatoria queda protegida del resto del alcance.
- Hay una regla clara para decidir en qué trabajar en cualquier momento de los cinco días, sin deliberar.
- El margen se aprovecha de forma incremental y en un orden pensado.
- La sustentación parte de un alcance declarado por escrito, lo que convierte una limitación en una decisión argumentada.
- Se demuestra capacidad de diseño sobre un alcance mayor que el implementado, que es justo lo que el enunciado valora.

**Negativas**

- La solución entregada tendrá funcionalidades del enunciado sin implementar, y hay que sostener esa decisión con argumentos.
- Priorizar es una apuesta: si el nivel 1 se cierra antes de lo previsto, parte del nivel 2 queda sin hacer por un orden que ya no era el óptimo.
- Mantener el documento de arquitectura al día con el estado real de cada requisito es trabajo adicional en cada cierre de funcionalidad.
- El nivel 3 obliga a documentar diseño que no se puede verificar con código, y por tanto a ser más cuidadoso al describirlo.

## Tácticas de Bass aplicadas

- **Gestionar el alcance** — frontera explícita y priorizada.
- **Reducir el tamaño del incremento** — se cierra y se despliega cada nivel antes de abrir el siguiente.
- **Detectar fallos pronto** — el nivel 1 desplegado y verificado antes de ampliar.

## Escenarios y requisitos relacionados

- `RF-07` — el núcleo protegido por esta decisión.
- Todos los demás requisitos funcionales quedan clasificados en uno de los tres niveles.
- `RN-01`, `RN-02`, `RT-05`.

## ADRs relacionados

- **ADR-0002** — el ahorro de tiempo que hace posible el nivel 2.
- **ADR-0011** — la primera funcionalidad del nivel 2.
- **ADR-0017** — las evidencias que deben estar listas antes de abrir el nivel 2.
- **ADR-0007** — el número propio de WhatsApp, que queda en el nivel 3.

## Notas de implementación

- El estado de cada requisito se mantiene en una tabla del plan de implementación, actualizada al cerrar cada funcionalidad. Esa tabla es lo que se consulta en la sustentación para responder «¿esto está hecho?» sin dudar.
- Al cerrar el nivel 1 se recogen **todas** las evidencias (CI, CD, Sonar, Trivy, correo, WhatsApp, push, auditoría, observabilidad y pruebas) antes de tocar el nivel 2. Si el tiempo se agota después, la entrega ya está completa.
- Las funcionalidades del nivel 3 se describen en el diseño con el mismo rigor que las implementadas, pero marcadas de forma visible: un lector no debe poder confundir lo diseñado con lo construido.

## Referencias

- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice* — arquitectura y línea base evolutiva.
- Brooks, F. *The Mythical Man-Month.*
