# Proxytrace — all-in-one image.
#
# The single released image: the whole product in one container.
#
#   docker run -d --name proxytrace -p 5101:80 -p 5102:8081 -v proxytrace:/data \
#     ghcr.io/nordsteinsoftware/proxytrace
#
# It supervises five processes: PostgreSQL, Redis, the API, the ingestion proxy, and the
# nginx that serves the SPA + the manual and reverse-proxies /api. Postgres and Redis are
# started only when the operator has not pointed the container at their own — setting
# ConnectionStrings__Default / Redis__ConnectionString skips the embedded one, which is what
# the production compose file in deploy/ does. All state lives under the /data volume.
#
# The per-service Dockerfiles (Proxytrace.Api, Proxytrace.Proxy, frontend) are still what the
# source-built dev/e2e/perf composes use; they are no longer published as release images.

# ── Frontend bundle ────────────────────────────────────────────────────────────────────
# --platform=$BUILDPLATFORM: node output is arch-independent, so multi-arch release builds
# run npm natively instead of under QEMU. Only the final stage is per-arch.
FROM --platform=$BUILDPLATFORM node:24-alpine AS frontend
ARG APP_VERSION=0.0.0-dev
ENV VITE_APP_VERSION=$APP_VERSION
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
# The build's prebuild step (gen:docs) reads the manual markdown to bake the Tracey docs
# index into the bundle, so the manual sources must be present here too.
COPY manual/ /manual/
RUN npm run build

# ── VitePress manual (served at /docs) ─────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:24-alpine AS manual
WORKDIR /manual
COPY manual/package.json manual/package-lock.json ./
RUN npm ci
COPY manual/ .
RUN npm run docs:build

# ── API + ingestion proxy ──────────────────────────────────────────────────────────────
# The SDK runs natively on the build host and cross-compiles for $TARGETARCH.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
ARG BUILD_CONFIGURATION=Release
# Release version injected by the release workflow (-p:Version); see Directory.Build.props.
ARG APP_VERSION=0.0.0-dev
# Compile-time license public-key override (comma-separated base64 SPKI). Empty ⇒ embedded
# production key. The dev/e2e/perf composes pass the test key; official images pass nothing.
ARG LICENSE_PUBLIC_KEY=
WORKDIR /src
# The Nordstein.Core reference expansion lives in these files, so restore needs them
# before the project files. Nordstein.Core.sln is what selects source mode (see
# Directory.Build.props); without it the image would silently restore package mode.
COPY ["Directory.Build.props", "Directory.Build.targets", "nuget.config", "./"]
COPY ["core/Directory.Build.props", "core/Nordstein.Core.sln", "core/"]
COPY ["core/Nordstein.Core.Common/Nordstein.Core.Common.csproj", "core/Nordstein.Core.Common/"]
COPY ["Proxytrace.Api/Proxytrace.Api.csproj", "Proxytrace.Api/"]
COPY ["Proxytrace.Proxy.Api/Proxytrace.Proxy.Api.csproj", "Proxytrace.Proxy.Api/"]
COPY ["Proxytrace.Proxy/Proxytrace.Proxy.csproj", "Proxytrace.Proxy/"]
COPY ["Proxytrace.Application/Proxytrace.Application.csproj", "Proxytrace.Application/"]
COPY ["Proxytrace.Domain/Proxytrace.Domain.csproj", "Proxytrace.Domain/"]
COPY ["Proxytrace.Infrastructure/Proxytrace.Infrastructure.csproj", "Proxytrace.Infrastructure/"]
COPY ["Proxytrace.Messaging/Proxytrace.Messaging.csproj", "Proxytrace.Messaging/"]
COPY ["Proxytrace.Serialization/Proxytrace.Serialization.csproj", "Proxytrace.Serialization/"]
COPY ["Proxytrace.Storage/Proxytrace.Storage.csproj", "Proxytrace.Storage/"]
RUN dotnet restore "Proxytrace.Api/Proxytrace.Api.csproj" -a $TARGETARCH \
 && dotnet restore "Proxytrace.Proxy.Api/Proxytrace.Proxy.Api.csproj" -a $TARGETARCH
COPY . .
# SkipManualBuild: the manual is built by the `manual` stage above and served by nginx, not
# from the API's wwwroot. DebugType=none: ship no PDBs (they carry a SourceLink map back to
# the repo + commit SHA); stack traces keep frames, lose file/line.
RUN dotnet publish "Proxytrace.Api/Proxytrace.Api.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION \
      -o /out/api /p:UseAppHost=false /p:SkipManualBuild=true -p:Version=$APP_VERSION \
      -p:DebugType=none "-p:LicensePublicKey=$LICENSE_PUBLIC_KEY" \
 && dotnet publish "Proxytrace.Proxy.Api/Proxytrace.Proxy.Api.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION \
      -o /out/proxy /p:UseAppHost=false -p:Version=$APP_VERSION \
      -p:DebugType=none "-p:LicensePublicKey=$LICENSE_PUBLIC_KEY"

# ── Runtime ────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# postgresql-16 matches the Postgres major the compose deployment runs, so a data directory
# is interchangeable between the two shapes. curl backs the healthcheck and the proxy's
# wait-for-api gate.
RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      postgresql-16 redis-server nginx supervisor curl ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && rm -f /etc/nginx/sites-enabled/default

# One nginx config for both shapes: the frontend image's, with its upstream repointed from
# the compose service name to the loopback API in this container. Editing frontend/nginx.conf
# keeps both in sync.
COPY frontend/nginx.conf /etc/nginx/conf.d/proxytrace.conf
RUN sed -i 's|http://api:8080|http://127.0.0.1:8080|g' /etc/nginx/conf.d/proxytrace.conf

COPY --from=frontend /app/dist /usr/share/nginx/html
COPY --from=manual /manual/.vitepress/dist /usr/share/nginx/html/docs
COPY --from=backend /out/api /opt/proxytrace/api
COPY --from=backend /out/proxy /opt/proxytrace/proxy

COPY deploy/allinone/entrypoint.sh deploy/allinone/wait-for.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/entrypoint.sh /usr/local/bin/wait-for.sh

# Everything that must survive a container replacement lives under /data: the Postgres
# cluster, the Lucene search index, and the appdata dir holding the generated session
# signing key, the Data Protection key ring (which encrypts stored provider keys) and the
# license offline-grace cache. The API and the proxy run as the image's non-root `app` user
# (uid 1654); Postgres runs as `postgres`.
ENV PROXYTRACE_DATA_DIR=/data/appdata \
    Search__IndexPath=/data/searchindex \
    PGDATA=/data/pgdata
VOLUME /data

# 80: web UI (SPA, /docs, /api, /mcp). 8081: the OpenAI-compatible ingestion proxy.
EXPOSE 80 8081

# Reports healthy only once the API has applied migrations and the proxy accepts calls.
# start-period covers first-boot initdb + migrations on a cold volume.
HEALTHCHECK --interval=15s --timeout=5s --start-period=90s --retries=5 \
  CMD curl -fsS http://127.0.0.1/api/health && curl -fsS http://127.0.0.1:8081/health || exit 1

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
