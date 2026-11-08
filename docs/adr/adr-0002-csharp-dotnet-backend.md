# ADR-0002: C# sobre .NET 10 como lenguaje y runtime del back-end

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0002 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB de plataforma de ejecución del back-end |

## Contexto

Hay cinco días hasta la entrega, el despliegue y la sustentación (RN-02), y todo debe ser comprendido y explicable por el autor (RN-01). La línea base obligatoria (RF-07) exige, en código funcionando: persistencia transaccional, correo, WhatsApp, notificación en tiempo real y auditoría, además de un pipeline con Sonar y Trivy (RT-03) y un despliegue automatizado (RT-04).

El autor domina dos plataformas: Java con Spring Boot, que es el estilo de referencia del curso, y C# sobre .NET, con el que construyó un proyecto anterior del mismo curso y el mismo docente. Ese proyecto anterior ya tiene resuelto y probado, con pruebas en verde, exactamente el conjunto de piezas que RF-07 requiere: Outbox transaccional con `FOR UPDATE SKIP LOCKED`, gateway de notificaciones multicanal, adaptador de WhatsApp Cloud API con clasificación de errores, auditoría con cadena de hashes, pipeline con Sonar, Trivy y escaneo de secretos, y Dockerfile de imágenes mínimas.

La primera versión de este ADR eligió Spring Boot por familiaridad con el estilo del curso. Se revisó el mismo día al inventariar los activos reutilizables disponibles.

## Decisión

Usar **C# sobre .NET 10** para el back-end, reutilizando como punto de partida los componentes de infraestructura ya construidos y probados por el autor, adaptados al dominio de Kudos.

Precisiones sobre la reutilización:

1. Se reutiliza el **patrón y la implementación de infraestructura** (Outbox, gateway, adaptador de WhatsApp, auditoría, pipeline, Dockerfile). El dominio de Kudos se escribe desde cero: no tiene nada que ver con el dominio anterior.
2. Se reutiliza el **formato** de los documentos y de los ADR, nunca su contenido. Este trabajo es individual (RN-01) y todo texto es propio y referido a Kudos.
3. Se **poda** lo que Kudos no necesita: no hay multi-tenancy, ni aprovisionamiento por línea de comandos, ni pasarela de borde. Reutilizar no es arrastrar.
4. Lo reutilizado debe quedar comprendido: cada pieza adaptada se acompaña de sus pruebas, que es la forma de demostrar que se entiende lo que hace.

## Alternativas consideradas

| Criterio | **C# / .NET 10 (elegida)** | A. Java con Spring Boot | B. Node.js con NestJS |
|---|---|---|---|
| Piezas de RF-07 ya resueltas y probadas | Cumple — Outbox, gateway, WhatsApp, auditoría y pipeline existen | No cumple — hay que escribirlas | No cumple |
| Alineación con el estilo de referencia del curso | Parcial — el estilo de puertos y adaptadores sí; el lenguaje no | Cumple | No cumple |
| Capacidad de sustentar (RN-01) | Cumple — el autor tomó cada decisión de esas piezas y están documentadas | Cumple | Parcial |
| Reglas de arquitectura automatizadas | Cumple — ArchUnitNET ya configurado | Parcial — ArchUnit existe, hay que configurarlo | No cumple — no hay equivalente maduro |
| Pruebas de integración con dependencias reales | Cumple — Testcontainers configurado | Cumple — Testcontainers existe, hay que configurarlo | Parcial |
| Soporte de Sonar y Trivy (RT-03) | Cumple | Cumple | Cumple |
| Viabilidad en 5 días (RN-02) | Cumple | No cumple con holgura | No cumple |
| Tipado fuerte del dominio | Cumple | Cumple | Parcial |

- **A. Java con Spring Boot** — descartada: es la opción más alineada con el estilo de referencia y el autor la domina, pero obliga a reescribir desde cero las cinco piezas que RF-07 exige. En cinco días ese costo compite directamente con la funcionalidad obligatoria, el despliegue y la sustentación.
- **B. Node.js con NestJS** — descartada: ni reutiliza nada ni es el estilo del curso, y el tipado dinámico debilita las invariantes del dominio que exige ADR-0001.

**Lectura:** la decisión se toma por el plazo, no por superioridad técnica del lenguaje. Java con Spring Boot resolvería el problema igual de bien con más tiempo disponible. Lo que inclina la balanza es que el 70 % de la infraestructura que exige la línea base ya está escrita, probada y sustentada por el autor en esta plataforma.

## Consecuencias

**Positivas**

- El tiempo se concentra en el dominio de Kudos, en el cliente móvil y en el despliegue, que es lo que no está hecho.
- Las piezas reutilizadas llegan con sus pruebas, lo que eleva la cobertura de la línea base desde el primer día.
- El pipeline con Sonar, Trivy y escaneo de secretos arranca funcionando, de modo que DRV-007 se cumple desde el inicio y no como un añadido final.
- Las imágenes base mínimas, sin consola ni gestor de paquetes, reducen los hallazgos de Trivy.

**Negativas**

- El lenguaje no coincide con el proyecto de referencia del curso, así que las equivalencias con el estilo visto en clase hay que explicarlas en la sustentación.
- Existe el riesgo de arrastrar conceptos del proyecto anterior que aquí no aplican; la poda del punto 3 de la decisión lo mitiga, y las pruebas de arquitectura lo detectan.
- Reutilizar sin comprender sería una falla frente a RN-01. La mitigación es que cada pieza adaptada se prueba.

## Tácticas de Bass aplicadas

- **Reutilizar componentes probados** — reduce el riesgo de defectos en el camino crítico.
- **Reducir el tamaño del cambio** — se adapta infraestructura existente en lugar de escribirla.
- **Abstraer servicios comunes** — las piezas reutilizadas ya están detrás de puertos.

## Escenarios y requisitos relacionados

- `RF-07` — la funcionalidad obligatoria es la que determina esta elección.
- `RN-01` — todo debe poder explicarse: por eso se reutiliza solo lo que el autor construyó.
- `RN-02` — el plazo es el criterio decisivo.
- `RT-03`, `RT-04`, `RT-06`.

## ADRs relacionados

- **ADR-0001** — las capas que este runtime materializa.
- **ADR-0003** — el acceso a datos sobre esta plataforma.
- **ADR-0017** — el pipeline que se reutiliza.
- **ADR-0019** — el alcance que este ahorro de tiempo hace posible.

## Notas de implementación

- Compilación sin advertencias como condición del pipeline: una advertencia tratada como error es una conversación que no hay que tener dos veces.
- Las imágenes de ejecución usan la variante mínima que incluye datos de zona horaria y de internacionalización, necesarios para formatear fechas y textos en español.
- Cada componente traído del proyecto anterior se registra en el documento de arquitectura indicando qué se adaptó y qué se descartó, para que la sustentación pueda recorrerlo.

## Referencias

- Microsoft. *.NET documentation.* learn.microsoft.com.
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice.*
