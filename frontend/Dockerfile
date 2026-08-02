# --platform=$BUILDPLATFORM: node build stages always run natively on the build host —
# the JS bundle output is arch-independent, so multi-arch release builds avoid running
# npm under QEMU emulation. Only the final nginx stage is per-arch (no compile there).
FROM --platform=$BUILDPLATFORM node:25-alpine AS build
ARG BUILD_CONFIGURATION=production
# Release version baked into the bundle as __APP_VERSION__ (vite define). The SPA primarily
# shows the backend-reported version from /api/config; this constant is the build's own stamp.
ARG APP_VERSION=0.0.0-dev
ENV VITE_APP_VERSION=$APP_VERSION
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
# The build's prebuild step (gen:docs) reads the manual markdown (../manual) to bake the
# Tracey docs index into the bundle, so the manual sources must be present in this stage.
COPY manual/ /manual/
RUN npm run build

# Build the VitePress user/operator manual served at /docs.
FROM --platform=$BUILDPLATFORM node:25-alpine AS manual
WORKDIR /manual
COPY manual/package.json manual/package-lock.json ./
RUN npm ci
COPY manual/ .
RUN npm run docs:build

FROM nginx:alpine AS final
EXPOSE 80
# Bake the nginx config so the released image is self-contained (no bind mount needed);
# dev/e2e composes may still override it with a volume mount.
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
COPY --from=manual /manual/.vitepress/dist /usr/share/nginx/html/docs
CMD ["nginx", "-g", "daemon off;"]
