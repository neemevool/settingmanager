# VTMF Settings Manager

Centralized management of Name-Value settings stored in MS SQL Server databases across multiple clients.
The stack has two containers:

| Service | Image | Port | Purpose |
|---|---|---|---|
| `vtmf-settings` | `vtmf/settingsmanager:v2.21` | `8080` | Blazor web UI |
| `vtmf-settings-mcp` | `vtmf/settingsmanager-mcp:v2.21` | `127.0.0.1:8081` | Optional MCP server for Claude Code |

Development and build instructions are in [VTMF.Settings/README.md](VTMF.Settings/README.md).

## 1. Create the Docker Compose stack

Create a folder (e.g. `vtmf-settings/`) and save this as `compose.yaml` in it:

```yaml
services:
  vtmf-settings:
    image: vtmf/settingsmanager:v2.21
    ports:
      - "8080:8080"
    env_file: settings.env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
    volumes:
      - data-protection-keys:/app/keys
      - ${SQL_SCRIPTS_PATH}:/app/sql-scripts

  vtmf-settings-mcp:
    image: vtmf/settingsmanager-mcp:v2.21
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

**Create the separate directory where place the files from `consultant` directory.** And run then setting management sessions from there. The `consultant` directory contains a setup which makes Claude to pick up the settingmanager mcp and plugins and context so you dont have to explain it every time. 


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

  Close the session or reconnect the mcp server:
  ```
  /mcp
  ```
  Then choose plugin:`vtmf-settings:vtmf-settings`, then Reconnect;
   

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
| Previews and changes | `create_plan`, `renumber_plan`, `get_plan`, `list_plans`, `apply_plan` |
| Undo | `rollback_plan`, `rollback_session` |
| Copying between clients | `compare_clients`, `clone_plan`, `legacy_env_migration_plan` |

Only `apply_plan` changes settings. Everything else reads or prepares.

Tools for features (see [7. Creating a feature](#7-creating-a-feature) and
[8. Applying a feature](#8-applying-a-feature)). Features are stored in the shared metadata database
(`DefaultMetaBase` in `settings.env`), so every client can use them:

| Group | Tools |
|---|---|
| Feature module (metadata database) | `feature_module_status`, `install_feature_module` |
| Feature catalog | `find_features`, `get_feature`, `save_feature`, `set_feature_status`, `delete_feature` |
| Applying | `feature_plan` (preview only; apply with `apply_plan`), `list_applied_features` |

Skills: `sm-module` (checks and installs the `sm` module and the feature module), `manage-settings`
(guides Claude when you want to change settings), `create-feature` (records reusable features) and
`apply-feature` (applies a feature to a customer process).

### Updating

Two parts must be updated together: the MCP image and the plugin.

1. MCP image: change the tag in `compose.yaml`, then run the `pull` and `up -d` commands from step 3.
2. Plugin: in Claude Code run `/plugin marketplace update vtmf`, then `/reload-plugins`.

Check the versions:

- `/plugin` shows the plugin version (currently 1.3.0).
- `module_status` shows the `sm` module version in a client database (currently 7). Ask
  `Is the sm module up to date for prod-client1?`
- `feature_module_status` shows the feature module version in the metadata database (currently 2). Ask
  `Is the feature module up to date?`

Features need an MCP image with client module 7 and feature (metabase) module 2. After updating the image,
the `sm-module` skill installs both the next time you work with a client or a feature.

If `module_status` reports:

- `module_outdated`: nothing to do by hand. The `sm-module` skill installs the new module the next time you
  work with that client (Claude Code asks permission first).
- `module_newer_than_tool`: the database is ahead of the MCP image. Update the image (step 1).

The feature module reports the same way (`feature_module_outdated`, `feature_module_newer_than_tool`).
`metabase_not_configured` means `settings.env` has no `DefaultMetaBase` block (step 2).

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
| `Add the barcode column after the document number in the document list of tproc.` | `get_settings` to see the existing columns, then `create_plan` with the new column placed after `DOCNR`; preview, applies after "yes". |
| `Move the date column first.` | `create_plan` with a move of that column (the whole column with its settings moves); Claude shows one "Move column" line. |
| `Remove the old price column.` | `create_plan` removing the column with all its settings and translations; Claude lists them and says how many. |
| `Tidy the layout table.` | `renumber_plan`; gathers each column's settings together and renumbers; preview, applies after "yes". |
| `Copy process tproc from prod-client1 to prod-client2.` | `compare_clients` and `clone_plan`; preview, then apply after "yes". |
| `Compare tproc between prod-client1 and prod-client2.` | `compare_clients`; lists the differences. Nothing is changed. |
| `Undo the last change.` | `rollback_plan` for the latest change. |
| `Undo everything I did in this session.` | `rollback_session`; reverts all changes of the session, newest first. |
| `Show what I changed in this session.` | `session_changes`. |
| `Make this session a feature.` | The `create-feature` skill: asks what the feature lets the end user do, proposes names, saves a draft (`save_feature`), then turns the session's changes into feature rows. |
| `Show unfinished features.` | `find_features` with status `Draft`; lists the drafts by description. |
| `Add the barcode button like on customer Y in sparelocateitems for prod-client1.` | The `apply-feature` skill: `list_applied_features` on Y first, asks which feature and its choices, previews with `feature_plan`, applies after "yes". |
| `Could the article get a barcode if it has none?` | The `apply-feature` skill: asks a few questions, `find_features`, presents matching features by description. |

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

## 7. Creating a feature

A feature is a reusable piece of behaviour that settings switch on, e.g. "Allows adding a barcode to an article
on the product card (sparelocateitems)". The code behind it must already exist. The feature records which
settings make it work, which choices the consultant has when applying it, and what must be done by hand
afterwards (e.g. a Directo hook in `dcquerysettings`).

Build and test the behaviour first: change the settings with Claude as in section 6 (one session). When it
works, say that it should be reusable, or start on your own ("make a feature from the barcode settings of
customer Y").

Sample conversation:

> **You:** Make this session a feature.
>
> **Claude:** What does it let the end user do, and where?
>
> **You:** The barcode thing on the product card.
>
> **Claude:** Suggested description: "Allows adding a barcode to an article on the product card
> (sparelocateitems)". Names: "Article barcode on product card", "Add barcode to article". Which one?
>
> **You:** The first.
>
> **Claude:** Saved as an unfinished feature. *(shows the session's changes as feature rows)* Which of these
> belong to the feature, and which values differ per customer?
>
> **You:** All of them. The not-found action differs: ask the operator, or show an error.
>
> **Claude:** *(turns the value into a choice with two described options, saves)* Anything to do outside the
> settings tables?
>
> **You:** A Directo hook in dcquerysettings.
>
> **Claude:** *(writes the step with SQL guarded by `IF NOT EXISTS`, saves)* Mark it finished?
>
> **You:** yes
>
> **Claude:** Finished. Consultants can now apply it.

How it works:

- **Description first.** Consultants find features by what they enable, so Claude insists on a short,
  user-facing description and helps with the name.
- **Saved at every step.** The feature is stored as a draft from the first step; `Show unfinished features`
  lists drafts, `Show the current state` shows the feature.
- **Only finished features can be applied.** Drafts are never applied; to try changes, change the settings
  with Claude as in section 6.
- **Choices.** Values that differ per customer become questions (`{{name}}` placeholders): text, number,
  yes/no, or a list of described options. One answer fills every setting that uses it, also inside setting
  names.
- **No branching.** Anything that needs conditions goes into the manual steps or a separate feature.
- **Manual steps** may contain SQL. Claude never runs it; the consultant does.

## 8. Applying a feature

Ask in the customer's words or point to a customer where it already works. You do not need to know feature
names.

Sample conversation:

> **You:** Add the barcode button like on customer Y in sparelocateitems for prod-client1.
>
> **Claude:** On Y this feature is applied: "Allows adding a barcode to an article on the product card
> (sparelocateitems)". Is this it?
>
> **You:** Yes.
>
> **Claude:** When the scanned barcode is not found: 1) ask the operator (default), 2) show an error?
>
> **You:** Ask the operator.
>
> **Claude:** What are you setting up? *(then shows the preview table; warns if the process code does not seem
> to read a server setting the feature sets; mentions one manual step)* Apply this?
>
> **You:** yes
>
> **Claude:** Applied. Reload the SPA to see the change. Step 1: add the Directo hook. Run this SQL in
> prod-client1: *(SQL)*. Tell me when it is done.

How it works:

- **Same safety as section 6.** Preview, apply only after "yes", `Undo that` reverts it.
- **Choices** are asked in plain words; check the setting names in the preview, since answers can become part
  of them.
- **Re-applying** a fixed feature shows only the differences.
- **Manual steps** are shown one by one with SQL prepared for the client. Claude never runs the SQL; you run it
  (Settingmanager module install or your own SQL tool) and confirm.
- `list_applied_features` shows which features a client has (`Which features are applied on prod-client1?`).
