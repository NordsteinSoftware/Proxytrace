FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
# /app/data holds the Data Protection key ring the proxy must read to decrypt the upstream provider
# key before replaying it — it is the SAME ring the API writes, so the composes mount the shared
# appdata volume here. Pre-create it (an empty named volume inherits the image dir's ownership on
# first use) and hand /app to the non-root app user.
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app
# Default the key-ring location to that directory, matching what every compose file already mounts.
# Without this variable the ring is in-memory only and the proxy cannot decrypt anything the API
# wrote, which surfaces only as upstream 401s. See docs/security.md.
ENV PROXYTRACE_DATA_DIR=/app/data
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# --platform=$BUILDPLATFORM: the SDK always runs natively on the build host and
# cross-compiles for $TARGETARCH (amd64/arm64), so multi-arch release builds avoid
# running the compiler under QEMU emulation. Only the runtime stages are per-arch.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
COPY ["core/Nordstein.Core.Domain/Nordstein.Core.Domain.csproj", "core/Nordstein.Core.Domain/"]
COPY ["Proxytrace.Proxy.Api/Proxytrace.Proxy.Api.csproj", "Proxytrace.Proxy.Api/"]
COPY ["Proxytrace.Proxy/Proxytrace.Proxy.csproj", "Proxytrace.Proxy/"]
COPY ["Proxytrace.Domain/Proxytrace.Domain.csproj", "Proxytrace.Domain/"]
COPY ["Proxytrace.Infrastructure/Proxytrace.Infrastructure.csproj", "Proxytrace.Infrastructure/"]
COPY ["Proxytrace.Licensing/Proxytrace.Licensing.csproj", "Proxytrace.Licensing/"]
COPY ["Proxytrace.Messaging/Proxytrace.Messaging.csproj", "Proxytrace.Messaging/"]
COPY ["Proxytrace.Serialization/Proxytrace.Serialization.csproj", "Proxytrace.Serialization/"]
COPY ["Proxytrace.Storage/Proxytrace.Storage.csproj", "Proxytrace.Storage/"]
RUN dotnet restore "Proxytrace.Proxy.Api/Proxytrace.Proxy.Api.csproj" -a $TARGETARCH
COPY . .
WORKDIR "/src/Proxytrace.Proxy.Api"
RUN dotnet build "./Proxytrace.Proxy.Api.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app/build -p:Version=$APP_VERSION "-p:LicensePublicKey=$LICENSE_PUBLIC_KEY"

FROM build AS publish
ARG TARGETARCH
ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=0.0.0-dev
ARG LICENSE_PUBLIC_KEY=
# DebugType=none: ship no PDBs — they carry line-accurate symbols and a SourceLink map
# pointing at the source repo + commit SHA (needless bloat). Trade-off: production stack traces lose
# file/line info (exception messages and frames remain).
RUN dotnet publish "./Proxytrace.Proxy.Api.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -p:Version=$APP_VERSION -p:DebugType=none "-p:LicensePublicKey=$LICENSE_PUBLIC_KEY"

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Proxytrace.Proxy.Api.dll"]
