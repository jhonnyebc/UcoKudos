# UCO Kudos — API

Plataforma institucional de reconocimientos de la Universidad Católica de Oriente. Este repositorio contiene el **back-end**; el cliente web vive en un repositorio aparte (RT-02).

**Ingeniería del Software II** — semestre 2-2026 · Autor: Jhonny Estiven Benítez Caro · Docente: Wider Farid Sánchez Garzón

## Qué hay aquí

| Carpeta | Contenido |
|---|---|
| `src/UcoKudos.Domain` | Entidades, objetos de valor, servicios de dominio y puertos de repositorio. No depende de nada |
| `src/UcoKudos.Application` | Casos de uso y puertos. Depende solo del dominio |
| `src/UcoKudos.Infrastructure` | Adaptadores de salida: persistencia, correo, WhatsApp, push, objetos |
| `src/UcoKudos.Api` | API HTTP. Conoce puertos de entrada y DTO |
| `tests/UcoKudos.UnitTests` | Base de la pirámide: dominio y casos de uso, sin infraestructura |
| `tests/UcoKudos.ArchitectureTests` | Las cinco reglas de dependencia de ADR-0001, verificadas |
| `tests/UcoKudos.IntegrationTests` | Contra PostgreSQL y almacenamiento reales en contenedores desechables |
| `docs/` | Documentación de la entrega. Empezar por [docs/adr/README.md](docs/adr/README.md) |

La dirección de las dependencias la impone el compilador, no la convención: cada capa es un proyecto y las referencias van en un solo sentido.

## Arrancar el entorno local

Requisitos: .NET SDK 10, Docker Desktop y Git.

```bash
cp .env.example .env
```

Rellenar en `.env` las variables marcadas como SECRETO. Las secretas no tienen valor por omisión: si faltan, la aplicación no arranca (ADR-0018).

```bash
docker compose up -d
```

```bash
dotnet run --project src/UcoKudos.Api
```

| Servicio | Dirección |
|---|---|
| API | http://localhost:8082 |
| Salud | http://localhost:8082/salud/vivo y `/salud/listo` |
| PostgreSQL | localhost:5435 |
| Correo capturado | http://localhost:8026 |
| Almacenamiento de objetos | http://localhost:9001 |
| Observabilidad | http://localhost:18890 |

Los puertos están elegidos para no chocar con otros entornos locales. El manual completo está en [docs/MANUAL-PUESTA-A-PUNTO.md](docs/MANUAL-PUESTA-A-PUNTO.md).

## Comandos

```bash
dotnet build UcoKudos.slnx -c Release
```

```bash
dotnet format UcoKudos.slnx --verify-no-changes
```

```bash
dotnet test tests/UcoKudos.UnitTests -c Release
```

```bash
dotnet test tests/UcoKudos.ArchitectureTests -c Release
```

```bash
dotnet test tests/UcoKudos.IntegrationTests -c Release --filter "Category=Integration"
```

Las pruebas de integración necesitan Docker Desktop encendido. Toda clase de `IntegrationTests` debe llevar la marca de categoría o el CI no la ejecuta, y el pipeline seguiría en verde sin haberla corrido.

## Reglas de trabajo

- Si una decisión no está en [docs/adr](docs/adr/README.md), se decide y se registra antes de implementarla.
- El alcance está declarado en ADR-0019. Nada del nivel 2 empieza antes de que el nivel 1 esté desplegado y con sus evidencias recogidas.
- Al añadir una regla de arquitectura, comprobarla provocando a propósito una violación: una regla que nunca ha fallado no está verificada.
- Los secretos no entran al repositorio. Hay un escaneo antes del envío y otro en el pipeline.
