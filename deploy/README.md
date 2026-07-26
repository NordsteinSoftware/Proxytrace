# Proxytrace — Docker Compose deployment

Runs Proxytrace with its database and Redis as separate, backup-able containers. The
application image (`ghcr.io/proxytrace/proxytrace`, also on Docker Hub as
`proxytrace/proxytrace`) contains the web UI, the API and the ingestion proxy; nothing is
built locally.

The same image also runs standalone with an embedded PostgreSQL — `docker run -d -p 5101:80
-p 5102:8081 -v proxytrace:/data ghcr.io/proxytrace/proxytrace` — which is the quickest way
to evaluate it. Use *this* compose file when you want the database on its own, so it can be
backed up, tuned and upgraded independently. It's the same image either way: setting
`ConnectionStrings__Default` (as this file does) is what keeps the embedded database off.

## Quickstart

```bash
docker compose up -d
```

That's it — no `.env` required. Open http://localhost:5101 and follow the first-run
setup. The user & operator manual is served at http://localhost:5101/docs.

To start capturing traces, point your agents' OpenAI base URL at the project-scoped
endpoint the setup wizard hands you: `http://localhost:5102/{project-slug}/openai/v1`.
The project segment is what attributes the call when the client keeps using your
upstream provider key.

## Configuration (optional)

Every setting has a working default. To override (ports, public URL, your own
database password — recommended for production):

```bash
cp .env.example .env
# edit .env, then:
docker compose up -d
```

The session signing key is generated on first start and persisted in the `appdata`
volume; logins survive restarts and upgrades without any configuration.

## Upgrading

```bash
# back up first: docker compose exec postgres pg_dump -U proxytrace proxytrace > backup.sql
docker compose pull
docker compose up -d
```

Database migrations apply automatically on startup. See the manual's
[Upgrading](http://localhost:5101/docs/admin/upgrading.html) page for details.

## License

Without a license, Proxytrace runs the Free tier. To activate a license key, either:

- enter it during the first-run setup wizard or under **Settings → License** (stored
  in the database, applies immediately — no restart), or
- set `PROXYTRACE_LICENSE` in `.env` and run `docker compose up -d`.

A key activated in the UI takes precedence over the environment variable.
