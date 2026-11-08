# Documentación de UCO Kudos

UCO Kudos es la plataforma institucional de reconocimientos de la Universidad Católica de Oriente: cualquier integrante de la comunidad académica publica un Kudo para otra persona, en una de las categorías institucionales, y el destinatario se entera por correo, por WhatsApp y por una notificación en su celular.

**Ingeniería del Software II** — semestre 2-2026 · Autor: Jhonny Estiven Benítez Caro · Docente: Wider Farid Sánchez Garzón

Toda la documentación está en Markdown (RT-01).

## Documentos

| Documento | Contenido |
|---|---|
| [adr/](adr/README.md) | Los **19 Architecture Decision Records**, con su contexto, alternativas comparadas, consecuencias y tácticas de Bass. Es el documento principal |
| [drivers-arquitectonicos.md](drivers-arquitectonicos.md) | Los 7 drivers del enunciado, 18 requisitos funcionales, 8 atributos de calidad priorizados, 12 escenarios de calidad con su medida y las restricciones técnicas y de negocio |
| [MANUAL-PUESTA-A-PUNTO.md](MANUAL-PUESTA-A-PUNTO.md) | Cómo levantar la plataforma desde cero y cómo conectar cada proveedor externo |
| [00-plan-de-decisiones.md](00-plan-de-decisiones.md) | Documento de trabajo: ruta de los cinco días y estado de las decisiones |
| [01-reutilizacion-flowride.md](01-reutilizacion-flowride.md) | Qué infraestructura propia de un proyecto anterior se reutiliza, qué se poda y qué se escribe desde cero. Sustenta ADR-0002 |

## Cómo leerla

1. **Qué condiciona la arquitectura:** los [drivers](drivers-arquitectonicos.md). Los dos que más pesan son que el 90 % de los usuarios entra desde un celular y que la carga es predominantemente de lectura.
2. **Cómo se respondió:** los [ADR](adr/README.md). El índice tiene una tabla que traza cada driver del enunciado con los ADR que lo atienden.
3. **La funcionalidad obligatoria:** ADR-0005 (una transacción y una bandeja de eventos), ADR-0006 (el punto único de salida), ADR-0007, ADR-0008 y ADR-0009 (los tres canales) y ADR-0013 (la auditoría).
4. **Qué está implementado:** ADR-0019 declara el alcance en tres niveles.

## Pendiente de escribir

Esta sección se vacía a medida que avanza la entrega.

- Documento de Arquitectura de Software que integre las vistas.
- Diagramas: C4 de nivel 1 y 2, componentes, paquetes, secuencias del flujo de publicación, despliegue y modelo entidad-relación.
- Matriz de evaluación de los doce factores, analizados y justificados uno por uno.
- Evidencias: CI, CD, Sonar, Trivy, correo, WhatsApp, push, auditoría, observabilidad y pruebas.
