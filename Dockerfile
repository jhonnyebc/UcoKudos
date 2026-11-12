# syntax=docker/dockerfile:1
# Imagen de la API de UcoKudos (ADR-0016).
#   docker build -t ucokudos-api:<versión>-<sha> .
#
# Versiones de las imágenes base fijas. La de ejecución es "chiseled": sin consola ni gestor de
# paquetes y con usuario sin privilegios, lo que reduce la superficie y los hallazgos de Trivy
# (ADR-0017). Variante "extra" porque trae ICU y la base de zonas horarias que necesitan los
# textos en español y el manejo de fechas.
ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0.403-noble
ARG ASPNET=mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled-extra

FROM ${SDK} AS compilacion
WORKDIR /src

# Primero solo los proyectos: la restauración queda en caché mientras no cambien las dependencias.
COPY Directory.Build.props ./
COPY src/UcoKudos.Domain/UcoKudos.Domain.csproj src/UcoKudos.Domain/
COPY src/UcoKudos.Application/UcoKudos.Application.csproj src/UcoKudos.Application/
COPY src/UcoKudos.Infrastructure/UcoKudos.Infrastructure.csproj src/UcoKudos.Infrastructure/
COPY src/UcoKudos.Api/UcoKudos.Api.csproj src/UcoKudos.Api/
RUN dotnet restore src/UcoKudos.Api/UcoKudos.Api.csproj

COPY src/ src/
RUN dotnet publish src/UcoKudos.Api/UcoKudos.Api.csproj -c Release --no-restore -o /publicado

FROM ${ASPNET} AS api
ARG VERSION=0.0.0-local
ARG REVISION=local
LABEL org.opencontainers.image.title="ucokudos-api" \
      org.opencontainers.image.description="API de la plataforma de reconocimientos UCO Kudos" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"
WORKDIR /app
COPY --from=compilacion /publicado .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "UcoKudos.Api.dll"]
