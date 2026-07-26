# Proxytrace

**Trace, test and improve your AI agents.** Proxytrace captures every LLM call your agents
make, turns real traces into regression suites, and closes the loop with evidence-backed
optimization proposals. Self-hosted, runs entirely on your own infrastructure.

- Website: <https://proxytrace.dev>
- Source & releases: <https://github.com/SyntaktikEU/Proxytrace>
- Docs: served at `/docs` in every install

## Run it

The image is the whole product — web UI, API, ingestion proxy, PostgreSQL and Redis in one
container. Nothing to configure:

```bash
docker run -d --name proxytrace \
  -p 5101:80 -p 5102:8081 \
  -v proxytrace:/data \
  proxytrace/proxytrace
```

Open <http://localhost:5101> and follow the first-run setup. Point your agent's OpenAI base
URL at the project-scoped endpoint the wizard shows —
`http://localhost:5102/{project-slug}/openai/v1` — and traces stream into the UI in real time.

All state (database, secrets, search index) lives in the `/data` volume — back that up, and
upgrade by pulling a newer tag and recreating the container. Schema migrations run on start.

## Bring your own Postgres

Set `ConnectionStrings__Default` (and optionally `Redis__ConnectionString`) and the container
skips its embedded services and uses yours instead. That is the recommended production shape,
and it's exactly what the Docker Compose deployment attached to every
[GitHub release](https://github.com/SyntaktikEU/Proxytrace/releases) does — a `proxytrace.zip`
with a pinned compose file (this image + Postgres + Redis) and an `.env` template:

```bash
curl -fLO https://github.com/SyntaktikEU/Proxytrace/releases/latest/download/proxytrace.zip
unzip proxytrace.zip && cd proxytrace-<version>
docker compose up -d        # no .env required — see .env.example for overrides
```

## Tags & platforms

**Tags:** `X.Y.Z` (immutable, pin this in production), plus rolling `X.Y`, `X` and `latest`.
Prereleases (`X.Y.Z-rc.N`) publish only their exact version.

**Platforms:** `linux/amd64`, `linux/arm64`.

The same image is published to GitHub Container Registry as `ghcr.io/proxytrace/proxytrace` —
identical digests, same tags. GHCR has no anonymous pull-rate limit.

## License

Source-available under the [Elastic License 2.0](https://github.com/SyntaktikEU/Proxytrace/blob/master/LICENSE):
you may use, copy, modify and redistribute it, but you may not offer it as a managed service to
third parties, circumvent the license-key functionality, or remove the licensing/copyright notices.

A free tier is built in; paid tiers unlock higher limits and additional features.
