# VTMF Settings Manager: development

For running the released images see the [root README](../README.md).

## Projects

| Project | Purpose |
|---|---|
| `VTMF.Settings` | Blazor Server web UI |
| `VTMF.Settings.Core` | Shared services, `sm` module scripts (`Sql/settingsmanagement/`) and installer |
| `VTMF.Settings.Mcp` | MCP server over the `sm` procedures |
| `VTMF.Settings.Tests` | Tests |
| `plugins/vtmf-settings` | Claude Code plugin (MCP connection + `sm-module` skill) |

## Build and test

Always use the wrapper from the repo root (direct `dotnet build` / `dotnet test` is blocked by a hook):

```bash
pwsh -NoProfile -File ./eng/verify.ps1 -Mode build
pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "<filter>"
pwsh -NoProfile -File ./eng/verify.ps1 -Mode warnings [-Target <project>]
pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate     # must pass before finishing
```

## Connections

Connections use `SqlConnectionParameters`, from user secrets in development or from environment variables /
`settings.env` (see the root README for the format).

## Run locally

```bash
cd VTMF.Settings
dotnet run --launch-profile http     # http://localhost:5045
dotnet run --launch-profile https    # https://localhost:7188
```

Docker, built from source (repo root, uses `compose.yaml`):

```bash
docker compose --env-file settings.env up -d --build
docker compose --env-file settings.env up -d --build vtmf-settings-mcp   # MCP server only
```

## MCP server locally (stdio)

Build once, then register the built dll. Don't use `dotnet run`: its build output corrupts stdio.

```bash
pwsh -NoProfile -File ./eng/verify.ps1 -Mode build
claude mcp add vtmf-settings -- dotnet C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings.Mcp\bin\Debug\net10.0\VTMF.Settings.Mcp.dll
```

Or against the Docker container (HTTP):

```bash
claude mcp add --transport http vtmf-settings http://localhost:8081/mcp
```

Remove the manual entry (`claude mcp remove vtmf-settings`) before installing the plugin.

## Change rules

- Any change under `VTMF.Settings.Core/Sql/settingsmanagement/` requires bumping `version` and updating `sha256`
  in `module.json`. The failing `RecordedHash_MatchesScripts` test shows the new hash as the Expected value in
  two 32-char halves; concatenate them. Scripts must be idempotent, no `BEGIN TRAN`.
- When anything under `plugins/vtmf-settings/` changes, bump `version` in
  `plugins/vtmf-settings/.claude-plugin/plugin.json`.

## Release: push to Docker Hub

From the repo root. Bump the tags, then update `composeprod.yaml` and the root `README.md` to match:

```bash
docker build --tag vtmf/settingsmanager:v2.17 --platform linux/amd64,linux/arm64 --attest type=sbom,generator=docker/scout-sbom-indexer:latest --pull --push .
docker build -f Dockerfile.mcp --platform linux/amd64,linux/arm64 --tag vtmf/settingsmanager-mcp:v2.17 --attest type=sbom,generator=docker/scout-sbom-indexer:latest --pull --push .
```

Production: `composeprod.yaml` (MCP bound to `127.0.0.1:8081`, the MCP server has no authentication).
