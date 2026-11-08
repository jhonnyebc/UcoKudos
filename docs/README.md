# Documentación de UCO Kudos

UCO Kudos es la plataforma institucional de reconocimientos de la Universidad Católica de Oriente: cualquier integrante de la comunidad académica publica un Kudo para otra persona, en una de las categorías institucionales, y el destinatario se entera por correo, por WhatsApp y por una notificación en su celular, con todo el proceso auditado.

**Ingeniería del Software II** — semestre 2-2026 · Autor: Jhonny Estiven Benítez Caro · Docente: Wider Farid Sánchez Garzón

Toda la documentación está en Markdown (RT-01). Los diagramas están escritos en Mermaid dentro de los propios documentos, así que GitHub los dibuja al abrir el archivo; los **21 diagramas** están validados con el parser oficial de Mermaid.

## Empezar por aquí

**[DAS.md](DAS.md)** — el Documento de Arquitectura de Software. Es el documento integrador: articula todos los demás y dice dónde está cada detalle. Si solo se va a leer una cosa, es esta.

## Documentos

### Motivadores y decisiones

| Documento | Contenido |
|---|---|
| [drivers-arquitectonicos.md](drivers-arquitectonicos.md) | Los 7 drivers del enunciado, 18 requisitos funcionales, 8 atributos de calidad priorizados, 12 escenarios de calidad con su medida, y las restricciones técnicas y de negocio |
| [adr/](adr/README.md) | Los **19 Architecture Decision Records**, con contexto, alternativas comparadas por atributo de calidad, consecuencias, tácticas de Bass y notas de implementación |
| [abb-sbb.md](abb-sbb.md) | **17 ABB** con el driver que los exige, y los SBB elegidos y descartados, evaluados por costo, complejidad, escalabilidad, riesgos, mantenibilidad y evolución |

### Diseño

| Documento | Contenido |
|---|---|
| [diagramas/01-diseno-alto-nivel-c4.md](diagramas/01-diseno-alto-nivel-c4.md) | **C4 nivel 1** (contexto) y **nivel 2** (contenedores) |
| [diagramas/02-diagrama-de-componentes.md](diagramas/02-diagrama-de-componentes.md) | C4 nivel 3: qué hay dentro de la API, con las responsabilidades de cada componente |
| [diagramas/03-diagrama-de-paquetes.md](diagramas/03-diagrama-de-paquetes.md) | Proyectos, espacios de nombres y quién hace cumplir cada regla de dependencia |
| [diagramas/04-diagramas-de-secuencia.md](diagramas/04-diagramas-de-secuencia.md) | **7 secuencias**: publicar un Kudo en dos mitades, el fallo de un canal, el muro, la carga de un adjunto, la aprobación de una cuenta y el veto en cascada |
| [diagramas/05-diagrama-de-despliegue.md](diagramas/05-diagrama-de-despliegue.md) | Despliegue en producción, entorno local y el camino del commit al servidor |
| [diagramas/modelo-de-datos.md](diagramas/modelo-de-datos.md) | Modelo entidad-relación en 3 diagramas, con las invariantes que defiende la base y los índices que sostienen el rendimiento |

### Análisis que pide el enunciado

| Documento | Contenido |
|---|---|
| [twelve-factor.md](twelve-factor.md) | Matriz de los doce factores, cada uno analizado, evaluado y justificado |
| [manifiesto-reactivo.md](manifiesto-reactivo.md) | Los cuatro rasgos: responsividad, resiliencia, elasticidad y orientación a mensajes |
| [desarrollo-seguro.md](desarrollo-seguro.md) | Autenticación, autorización, secretos, validaciones, APIs, integraciones, matriz de **15 riesgos** y mapeo al OWASP Top 10 |
| [patrones-y-clean-code.md](patrones-y-clean-code.md) | **11 patrones aplicados** con su beneficio y **7 descartados** con su razón, los 5 principios SOLID y Clean Code |
| [piramide-de-pruebas.md](piramide-de-pruebas.md) | Los 4 niveles de prueba y la trazabilidad de cada escenario de calidad a la prueba que lo respalda |

### Operación

| Documento | Contenido |
|---|---|
| [MANUAL-PUESTA-A-PUNTO.md](MANUAL-PUESTA-A-PUNTO.md) | Cómo levantar la plataforma desde cero, cómo conectar cada proveedor externo y los problemas conocidos |

### Documentos de trabajo

| Documento | Contenido |
|---|---|
| [00-plan-de-decisiones.md](00-plan-de-decisiones.md) | Ruta de trabajo y estado de las decisiones |
| [01-reutilizacion-flowride.md](01-reutilizacion-flowride.md) | Qué infraestructura propia de un proyecto anterior se reutiliza, qué se poda y qué se escribe desde cero. Sustenta ADR-0002 |

## Cómo leerla

1. **Qué condiciona la arquitectura:** sección 2 del [DAS](DAS.md) o, con más detalle, los [drivers](drivers-arquitectonicos.md). Los dos que más pesan son que cerca del 90 % de los usuarios entra desde un celular y que la carga es predominantemente de lectura.
2. **Cómo se respondió:** secciones 3 a 6 del DAS y los [ADR](adr/README.md). El índice de los ADR traza cada driver con las decisiones que lo atienden.
3. **La funcionalidad obligatoria:** ADR-0005 es el corazón (una transacción y una bandeja de eventos), ADR-0006 el punto único de salida, ADR-0007 a ADR-0009 los tres canales, y ADR-0013 la auditoría. Las secuencias 1 y 2 lo muestran paso a paso.
4. **Qué está implementado:** sección 16 del DAS y [ADR-0019](adr/adr-0019-alcance-linea-base.md), que declara el alcance en tres niveles.

## Tres cosas que vale la pena mirar

**La funcionalidad obligatoria no ocurre en un solo paso.** Los cinco pasos que exige el enunciado se reparten entre dos secuencias: en la transacción ocurren la persistencia y la auditoría; el correo, el WhatsApp y el push ocurren después, en un proceso en segundo plano. Esa separación es deliberada y es lo que hace que publicar responda rápido y que un proveedor caído no impida publicar.

**Clean Architecture está verificada, no afirmada.** Hay pruebas que fallan si el dominio referencia infraestructura. Y la única regla que el compilador no puede garantizar se comprobó provocando a propósito una violación: una regla que nunca ha fallado no está verificada.

**Las limitaciones están declaradas.** Dos de los doce factores se cumplen parcialmente, dos rasgos del Manifiesto Reactivo quedan cortos, y el riesgo de pérdida total por tener una sola máquina queda en alto. Todas vienen del presupuesto cero, y en cada caso se dice cuál es el punto de cambio.

## Estado de la documentación

Completa. Lo que falta de la entrega no es documentación:

- **Evidencias:** CI, CD, Sonar, Trivy, correo, WhatsApp, push, auditoría, observabilidad y pruebas. Se recogen a medida que se generan, no al final.
- **Implementación y despliegue** de la línea base, según el alcance de ADR-0019.
