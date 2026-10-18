# VTMF Settings Manager

Centralized management of Name-Value settings stored in MS SQL Server databases across multiple clients.
The stack has two containers:

| Service | Image | Port | Purpose |
|---|---|---|---|
| `vtmf-settings` | `vtmf/settingsmanager:v2.17` | `8080` | Blazor web UI |
| `vtmf-settings-mcp` | `vtmf/settingsmanager-mcp:v2.17` | `127.0.0.1:8081` | Optional MCP server for Claude Code |

Development and build instructions are in [VTMF.Settings/README.md](VTMF.Settings/README.md).

## 1. Create the Docker Compose stack

Create a folder (e.g. `vtmf-settings/`) and save this as `compose.yaml` in it:

```yaml
services:
  vtmf-settings:
    image: vtmf/settingsmanager:v2.17
    ports:
      - "8080:8080"
    env_file: settings.env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
    volumes:
      - data-protection-keys:/app/keys
      - ${SQL_SCRIPTS_PATH}:/app/sql-scripts

  vtmf-settings-mcp:
    image: vtmf/settingsmanager-mcp:v2.17
    ports:
      - "127.0.0.1:8081:8080"   # loopback only: the MCP server has no authentication
    env_file: settings.env

volumes:
  data-protection-keys:
```

The MCP service is optional; remove it if you don't use Claude Code.

## 2. Set up the environment file

Create `settings.env` next to `compose.yaml`. It holds one block per client database, numbered from `0`
with no gaps:

```env
# Host folder with SQL scripts, mounted into the web app at /app/sql-scripts
SQL_SCRIPTS_PATH=C:\path\to\sql-scripts

# Metadata database (feature and process definitions), usually shared by all clients
SQLCONNECTIONPARAMETERS__0__CLIENTNAME=DefaultMetaBase
SQLCONNECTIONPARAMETERS__0__SERVER=Server=tcp:<host>,<port>;Initial Catalog=<database>;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__0__USERID=<sql-login>
SQLCONNECTIONPARAMETERS__0__PASSWORD=<password>

# A client database
SQLCONNECTIONPARAMETERS__1__CLIENTNAME=prod-client1
SQLCONNECTIONPARAMETERS__1__SERVER=Server=tcp:<host>,<port>;Initial Catalog=<database>;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__1__USERID=<sql-login>
SQLCONNECTIONPARAMETERS__1__PASSWORD=<password>
```

- `CLIENTNAME` is the name shown in the UI and used by the MCP tools.
- Changes are attributed to the SQL login of the connection, so use personal logins.
- `settings.env` contains passwords. Keep it out of git.

## 3. Run the stack

From the folder with `compose.yaml` and `settings.env`:

```bash
docker compose --env-file settings.env pull
docker compose --env-file settings.env up -d
```

`--env-file settings.env` makes `SQL_SCRIPTS_PATH` available to the `compose.yaml` volume mapping; `env_file:`
in the services passes the connection settings to the containers.

Check that it runs:

```bash
docker compose --env-file settings.env ps
docker compose --env-file settings.env logs -f vtmf-settings
```

Open http://localhost:8080.

To upgrade, change the image tags in `compose.yaml` and run the `pull` and `up -d` commands again.
To stop: `docker compose --env-file settings.env down`.

## 4. Optional: MCP server and Claude Code plugin

The `vtmf-settings-mcp` container exposes the settings as MCP tools at `http://localhost:8081/mcp`.
The `vtmf-settings` Claude Code plugin connects to it and adds the `sm-module` skill, which checks and installs
the `sm` module (stored procedures) in a client database before Claude works with it.

1. Make sure the MCP container is running (step 3).
2. The repo `github.com/neemevool/settingmanager` is private. Git must be able to clone it without a prompt,
   e.g. `gh auth login` then `gh auth setup-git`, or an SSH key / credential helper.
3. If you added the MCP server by hand earlier, remove it first, otherwise the tools appear twice:
   ```bash
   claude mcp remove vtmf-settings
   ```
4. In Claude Code:
   ```
   /plugin marketplace add neemevool/settingmanager
   /plugin install vtmf-settings@vtmf
   /reload-plugins
   ```
5. Check the connection with `/mcp`: `vtmf-settings` should be listed as connected.

Update the plugin with `/plugin marketplace update vtmf`, then `/reload-plugins`.
If the MCP server is not at `http://localhost:8081/mcp`, set `VTMF_SETTINGS_MCP_URL` before starting Claude Code.
For a host other than localhost/127.0.0.1, also add that host to `AllowedHosts` (environment variable) of the
MCP container.

Tools: `list_clients`, `module_status`, `install_module`, `list_processes`, `list_tables`, `get_settings`,
`get_effective_settings`, `settings_usage`, `legacy_env_rows`. `ServerSetting` values are always masked.

## 5. Sample prompts

| Prompt | What should happen |
|---|---|
| `Which VTMF clients are configured?` | Calls `list_clients`; lists the `CLIENTNAME` values from `settings.env`. |
| `Is the sm module installed for prod-client1?` | Calls `module_status`; reports `not_installed`, `up_to_date`, `outdated` or `newer_than_tool`. |
| `Show the Email settings for prod-client1.` | The `sm-module` skill runs first: `module_status`, and `install_module` if the module is missing or outdated (Claude Code asks permission before running it). Then `get_settings`. |
| `What are the effective settings for process X in prod-client1?` | `list_processes` to find the process, then `get_effective_settings`. |
| `Where is the setting Email.SMTP.Server used in prod-client1?` | `settings_usage`. |

How to check it works:

- Claude's reply shows the tool calls (`vtmf-settings - list_clients`, ...). If it answers without tool calls,
  run `/mcp` and check that `vtmf-settings` is connected.
- When the `sm-module` skill is used, Claude says it is checking the module and reports the state, e.g.
  `not_installed -> 2`.
- `unknown_client`: use the exact name from `list_clients`.
- `sm_error` on every call: the SQL server is unreachable or the login is wrong. Check `settings.env` and
  `docker compose --env-file settings.env logs vtmf-settings-mcp`.
- `module_newer_than_tool`: the database has a newer `sm` module than the MCP image. Update the image tag in
  `compose.yaml` and run the `pull` and `up -d` commands again.
