# =============================================================================
# Stage 1: Build React 18 + TypeScript Frontend (Sonarr v5 UI Shell)
# =============================================================================
FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci || npm install
COPY frontend/ ./
RUN npm run build

# =============================================================================
# Stage 2: Build .NET 10.0 Backend (Extractarr / Servarr Modular Monolith)
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY Directory.Build.props Extractarr.sln ./
COPY src/Extractarr.Common/Extractarr.Common.csproj src/Extractarr.Common/
COPY src/Extractarr.SignalR/Extractarr.SignalR.csproj src/Extractarr.SignalR/
COPY src/Extractarr.Core/Extractarr.Core.csproj src/Extractarr.Core/
COPY src/Extractarr.Api/Extractarr.Api.csproj src/Extractarr.Api/
COPY src/Extractarr.Host/Extractarr.Host.csproj src/Extractarr.Host/
COPY src/Extractarr.Integration.Test/Extractarr.Integration.Test.csproj src/Extractarr.Integration.Test/
RUN dotnet restore src/Extractarr.Host/Extractarr.Host.csproj

COPY src/ src/
RUN dotnet publish src/Extractarr.Host/Extractarr.Host.csproj -c Release -o /app/publish /p:UseAppHost=false

# =============================================================================
# Stage 3: Final Runtime Image (LinuxServer.io / Servarr PUID/PGID/UMASK Standard)
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV PUID=1000 \
    PGID=1000 \
    UMASK=002 \
    TZ=UTC \
    EXTRACTARR_PORT=8686 \
    EXTRACTARR_DATA_DIR=/config \
    UNPACKARR_PORT=8686 \
    UNPACKARR_DATA_DIR=/config \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update && \
    apt-get install -y --no-install-recommends gosu tzdata ca-certificates curl && \
    rm -rf /var/lib/apt/lists/* && \
    (getent group 1000 || groupadd -g 1000 extractarr) && \
    (getent passwd 1000 || useradd -u 1000 -g 1000 -d /config -s /bin/bash extractarr) && \
    mkdir -p /config /downloads /app/wwwroot

COPY --from=backend-build /app/publish /app
COPY --from=frontend-build /src/frontend/dist /app/wwwroot
COPY docker/entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh && chmod +x /entrypoint.sh

VOLUME ["/config", "/downloads"]
EXPOSE 8686

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -fsS "http://127.0.0.1:${EXTRACTARR_PORT:-8686}/api/v3/system/status" || exit 1

ENTRYPOINT ["/entrypoint.sh"]

