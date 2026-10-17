---
name: sm-module
description: Use before working with a VTMF client's settings through the vtmf-settings MCP tools, or when a tool returns module_not_installed, module_outdated or module_newer_than_tool.
---

# sm module check and install

The `sm` module (stored procedures) must be installed in a client's database, at the version the MCP server ships, before any settings tool works. The tools enforce this; this skill tells you how to get there. All tools return `{ ok, data, error: { code, message } }`.

1. If the client is not known, call `list_clients`.
2. Call `module_status(client)`. `data.state` is one of `not_installed`, `up_to_date`, `outdated`, `newer_than_tool` (see also `installed`, `version`, `availableVersion`).
3. `up_to_date`: continue with the task.
4. `not_installed` or `outdated`: call `install_module(client)`. Report `previousState` / `previousVersion` -> `installedVersion` to the user, then continue with the original task.
5. `newer_than_tool`: stop. The database module is newer than this MCP server. Tell the user to update the MCP server image (`docker compose pull`, or a new tag in `composeprod.yaml`).
6. `install_failed`: show the failing script and the SQL message from `error.message`, then stop. Do not retry in a loop.
7. `sm_error` on every call: the SQL server or login is unreachable. Tell the user; do not try to install.
8. `unknown_client`: call `list_clients` and use an exact client name.
9. If any later tool returns `module_not_installed`, `module_outdated` or `module_newer_than_tool`, go back to step 2.
