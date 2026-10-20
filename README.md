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

If you want to repull the images:

```bash
docker compose --env-file settings.env up -d --pull always
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
   
   When there are updates to the plugin, update with:
   ```
   /plugin marketplace update vtmf
   /reload-plugins
   ```
   
5. Check the connection with `/mcp`: `vtmf-settings` should be listed as connected.

If the MCP server is not at `http://localhost:8081/mcp`, set `VTMF_SETTINGS_MCP_URL` before starting Claude Code.
For a host other than localhost/127.0.0.1, also add that host to `AllowedHosts` (environment variable) of the
MCP container.

Tools for reading: `list_clients`, `module_status`, `install_module`, `list_processes`, `list_tables`,
`get_settings`, `get_effective_settings`, `settings_usage`, `legacy_env_rows`.
`ServerSetting` values are always masked.

Tools for changing settings (see [6. Changing settings with Claude](#6-changing-settings-with-claude)):

| Group | Tools |
|---|---|
| Sessions (what you changed, and why) | `start_session`, `list_sessions`, `session_changes`, `close_session` |
| Previews and changes | `create_plan`, `get_plan`, `list_plans`, `apply_plan` |
| Undo | `rollback_plan`, `rollback_session` |
| Copying between clients | `compare_clients`, `clone_plan`, `legacy_env_migration_plan` |

Only `apply_plan` changes settings. Everything else reads or prepares.

Skills: `sm-module` (checks and installs the `sm` module) and `manage-settings` (guides Claude when you
want to change settings).

### Updating

Two parts must be updated together: the MCP image and the plugin.

1. MCP image: change the tag in `compose.yaml`, then run the `pull` and `up -d` commands from step 3.
2. Plugin: in Claude Code run `/plugin marketplace update vtmf`, then `/reload-plugins`.

Check the versions:

- `/plugin` shows the plugin version (currently 1.1.1).
- `module_status` shows the `sm` module version in a client database (currently 5). Ask
  `Is the sm module up to date for prod-client1?`

If `module_status` reports:

- `module_outdated`: nothing to do by hand. The `sm-module` skill installs the new module the next time you
  work with that client (Claude Code asks permission first).
- `module_newer_than_tool`: the database is ahead of the MCP image. Update the image (step 1).

## 5. Sample prompts

| Prompt | What should happen |
|---|---|
| `Which VTMF clients are configured?` | Calls `list_clients`; lists the `CLIENTNAME` values from `settings.env`. |
| `Is the sm module installed for prod-client1?` | Calls `module_status`; reports `not_installed`, `up_to_date`, `outdated` or `newer_than_tool`. |
| `Show the Email settings for prod-client1.` | The `sm-module` skill runs first: `module_status`, and `install_module` if the module is missing or outdated (Claude Code asks permission before running it). Then `get_settings`. |
| `What are the effective settings for process X in prod-client1?` | `list_processes` to find the process, then `get_effective_settings`. |
| `Where is the setting Email.SMTP.Server used in prod-client1?` | `settings_usage`. |
| `Set Email.SMTP.Server to smtp.example.com in prod-client1.` | Claude shows a preview table and waits for your "yes" before changing anything. |
| `Add a LOT column after DOCNR in the document list of tproc` | Claude finds the current settings, asks what you are setting up, shows a preview, applies after "yes". |
| `Copy process tproc from prod-client1 to prod-client2.` | `compare_clients` and `clone_plan`; preview, then apply after "yes". |
| `Compare tproc between prod-client1 and prod-client2.` | `compare_clients`; lists the differences. Nothing is changed. |
| `Undo the last change.` | `rollback_plan` for the latest change. |
| `Undo everything I did in this session.` | `rollback_session`; reverts all changes of the session, newest first. |
| `Show what I changed in this session.` | `session_changes`. |

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

## 6. Changing settings with Claude

You do not need to ask for a "plan". Ask Claude to show the settings you are interested in, then say what to
change. Claude prepares the change, shows it to you, and applies it only after you confirm.

Sample conversation:

> **You:** Show the document list columns of tproc in prod-client1.
>
> **Claude:** *(shows the current columns: DOCNR, DATE, CUSTOMER, ...)*
>
> **You:** Add a LOT column after DOCNR.
>
> **Claude:** What are you setting up? *(a short name, e.g. "LOT column in document list"; it labels your
> changes so you can find or undo them later)*
>
> **You:** LOT column in document list.
>
> **Claude:** *(shows a preview table: each setting, its current value and the new value)* Apply this?
>
> **You:** yes
>
> **Claude:** Applied. Reload the SPA to see the change.
>
> **You:** Undo that.
>
> **Claude:** *(reverts the change and confirms)*

How it works:

- **Preview.** Nothing changes until you say yes. Read the table before you confirm.
- **"Stale".** If someone changed the same settings after the preview was made, Claude reports the preview
  as stale. Nothing was changed. Claude reads the settings again and shows a new preview. Undo never reverts
  only part of a change: it either goes through completely or not at all.
- **Undo.** `Undo that` reverts one change. `Undo everything I did in this session` reverts all changes made
  since you named the work, newest first.
- **New tables.** If a table was created during the work and is later rolled back, it is first backed up as
  `bck_{table}_{yyyyMMdd_HHmmss}` and then dropped. The data is not lost.
- **Who changed what.** Every change is recorded with your SQL login.
- **Settings UI.** Changes made by hand in the Settings UI are not part of sessions and cannot be undone
  with these commands.
- After a change, reload the SPA to see it.
