---
name: sm-module
description: Use before working with a VTMF client's settings through the vtmf-settings MCP tools, or when a tool returns module_not_installed, module_outdated, module_newer_than_tool, feature_module_not_installed, feature_module_outdated, feature_module_newer_than_tool or metabase_not_configured.
---

# sm module check and install

The `sm` module (stored procedures) must be installed in a client's database, at the version the MCP server ships, before any settings tool works. The tools enforce this; this skill tells you how to get there. All tools return `{ ok, data, error: { code, message } }`.

1. If the client is not known, call `list_clients`.
2. Call `module_status(client)`. `data.state` is one of `not_installed`, `up_to_date`, `outdated`, `newer_than_tool` (see also `installed`, `version`, `availableVersion`).
3. `up_to_date`: continue with the task.
4. `not_installed` or `outdated`: call `install_module(client)`. Report `previousState` / `previousVersion` -> `installedVersion` to the user, then continue with the original task.
5. `newer_than_tool`: stop. The database module is newer than this MCP server. Tell the user to update the MCP server image. Prod: pull the newer image tag in `composeprod.yaml` (`docker compose -f composeprod.yaml pull && docker compose -f composeprod.yaml up -d`). Dev: `docker compose up -d --build vtmf-settings-mcp`.
6. `install_failed`: show the failing script and the SQL message from `error.message`, then stop. Do not retry in a loop.
7. `sm_error` on every call: the SQL server or login is unreachable. Tell the user; do not try to install.
8. `unknown_client`: call `list_clients` and use an exact client name.
9. If any later tool returns `module_not_installed`, `module_outdated` or `module_newer_than_tool`, go back to step 2.

## Feature module (shared metadata database)

The feature tools (`find_features`, `get_feature`, `save_feature`, `set_feature_status`, `delete_feature`, `feature_plan`, `list_applied_features`) also need the feature module in the shared metadata database (`DefaultMetaBase`). It is versioned separately from the client `sm` module. `feature_module_status()` reports the same states as `module_status`.

| Code / state | What to do |
|---|---|
| `feature_module_not_installed` / `feature_module_outdated` (state `not_installed` / `outdated`) | Call `install_feature_module()`. Report `previousState` / `previousVersion` -> `installedVersion`, then continue. |
| `feature_module_newer_than_tool` (state `newer_than_tool`) | Stop. The metabase module is newer than this MCP server; tell the user to update the MCP server image (same commands as step 5). |
| `metabase_not_configured` | Stop. The MCP server has no `DefaultMetaBase` connection. Tell the user to add a connection block with `CLIENTNAME=DefaultMetaBase` (server, user, password) to `settings.env` and restart the MCP container. |
| `install_failed` | As step 6. |

Then continue with the manage-settings skill (or `create-feature` / `apply-feature`) for the user's actual task.
