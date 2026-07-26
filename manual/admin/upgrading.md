# Upgrading

Proxytrace releases follow [semantic versioning](https://semver.org). Release notes for every
version are published on the [GitHub releases page](https://github.com/SyntaktikEU/Proxytrace/releases).

When a newer release is available, admins see a notice in the app — see
[Update notifications](/admin/configuration#update-notifications) for how that works and how
to disable it.

## Before you upgrade: back up

Database migrations are **forward-only**. Rolling back a release means restoring a database
backup, so take one first:

```bash
# Single container (embedded database)
docker exec proxytrace pg_dump -U proxytrace proxytrace > proxytrace-backup.sql

# Docker Compose (database in its own container)
docker compose exec postgres pg_dump -U proxytrace proxytrace > proxytrace-backup.sql
```

## Upgrading

Pull the new image and recreate the container. The application applies any pending database
migrations automatically on startup; no manual migration step exists.

```bash
# Single container — replace <version> with the release you want
docker pull ghcr.io/proxytrace/proxytrace:<version>
docker rm -f proxytrace
docker run -d --name proxytrace -p 5101:80 -p 5102:8081 -v proxytrace:/data \
  ghcr.io/proxytrace/proxytrace:<version>

# Docker Compose — the artifact's compose file pins the version; edit the tag
# (or download the new release's artifact, which ships it pre-pinned), then:
docker compose pull
docker compose up -d
```

Removing and recreating the container is safe: everything that matters lives in the volumes —
`/data` in the single-container shape (database, secrets, search index), `pgdata` + `appdata`
in the Compose one — and survives. The search index is forward-compatible; if it ever
misbehaves after an upgrade, rebuild it from **Settings → Search indexing**.

::: tip Pin versions in production
Prefer pinned tags (`:1.2.3`) over `:latest` in production so upgrades are deliberate. The
rolling tags `:1.2` and `:1` track the newest patch/minor release respectively.
:::

## Rolling back

Restore the backup, then start the previous version:

```bash
docker compose down
docker compose up -d postgres
docker compose exec -T postgres psql -U proxytrace proxytrace < proxytrace-backup.sql
docker compose up -d          # with the older tag pinned in the compose file
```

Never run an older application version against a database that a newer version has already
migrated.

## PostgreSQL major versions

Proxytrace pins PostgreSQL to a major version — 16, both for the database embedded in the
image and for the `postgres:16-alpine` container in the Compose deployment. A PostgreSQL
**major** upgrade (16 → 17) is never just a tag bump: it requires a dump/restore or
`pg_upgrade`. Releases will note when a newer PostgreSQL major is supported; until then, keep
the pinned major.
