## Push to dockerhub

```bash
docker build --tag vtmf/settingsmanager:v2.16 --attest type=sbom,generator=docker/scout-sbom-indexer:latest --pull --push .
```
## Docker compose stack

```yaml
services:
  vtmf-settings:
    image: vtmf/settingsmanager:v2.16
    ports:
      - "8080:8080"
    env_file: settings.env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
    volumes:
      - data-protection-keys:/app/keys

volumes:
  data-protection-keys:
```

### Environment variables in settings.env
```env
ENVVAR=Loaded
SQLCONNECTIONPARAMETERS__0__CLIENTNAME=DefaultMetaBase
SQLCONNECTIONPARAMETERS__0__SERVER=Server=tcp:demoterminal.vtmf.ee,7777;Initial Catalog=dev_iconfit;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__0__USERID=henryvool
SQLCONNECTIONPARAMETERS__0__PASSWORD=********

SQLCONNECTIONPARAMETERS__1__CLIENTNAME=prod-hydraulic
SQLCONNECTIONPARAMETERS__1__SERVER=Server=tcp:a320ladu.pilveserv.eu,9999;Initial Catalog=prod_hydraulic;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__1__USERID=henryvool
SQLCONNECTIONPARAMETERS__1__PASSWORD=**********
```

## Settings management MCP server (VTMF.Settings.Mcp)

Read-only MCP tools over the `sm` procedures (phase 1). Each client database needs the
`settingsmanagement` install set (Installer page) before its tools work; `module_status` reports it.

Connections are configured exactly like VTMF.Settings (`SqlConnectionParameters` via user secrets or
environment variables / `settings.env`). Modifications are attributed to the SQL login of the connection.

Local (stdio) — build once, then register the built dll (not `dotnet run`, whose build output would corrupt stdio):

    dotnet build VTMF.Settings.Mcp
    claude mcp add vtmf-settings -- dotnet C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings.Mcp\bin\Debug\net10.0\VTMF.Settings.Mcp.dll

Docker (HTTP):

    docker compose up -d vtmf-settings-mcp
    claude mcp add --transport http vtmf-settings http://localhost:8081/mcp

Tools: list_clients, module_status, list_processes, list_tables, get_settings, get_effective_settings,
settings_usage, legacy_env_rows. ServerSetting values are always masked.

Claude Code plugin (recommended). The repo is a plugin marketplace; the `vtmf-settings` plugin bundles the
HTTP MCP connection (`http://localhost:8081/mcp`) and the `sm-module` skill, which guides Claude to check
and install the `sm` module for a client before working with it:

    /plugin marketplace add neemevool/settingmanager
    /plugin install vtmf-settings@vtmf

Update with `/plugin marketplace update vtmf` (then `/reload-plugins`). To use a different MCP URL, set the
`VTMF_SETTINGS_MCP_URL` environment variable before starting Claude Code. Remove any manually added
`vtmf-settings` MCP entry (`claude mcp remove vtmf-settings`) first, otherwise the tools appear twice.
