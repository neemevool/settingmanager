# Settings management — Phase 1.1: `sm` module install & upgrade via MCP

Date: 2026-10-17
Status: ready for approval
Builds on: `docs/superpowers/plans/2026-10-16-settings-management-phase1.md`, `docs/superpowers/specs/2026-10-16-settings-management-tools-design.md`

## Intent

Phase 1 delivered the `sm` SQL schema (source in the `database5` repo) and a read-only MCP server (`VTMF.Settings.Mcp`) over it. Nothing can actually put `sm` into a client database today: the `install.xml` packaged into the Docker images has no usable `settingsmanagement` set, and the scripts are not shipped in any image.

Phase 1.1 makes the MCP server able to **install and upgrade** the `sm` module in a client database, and makes every MCP tool **refuse safely** when the module is missing, outdated, or newer than the tool. A Claude Code **skill**, distributed as a plugin from this GitHub repo, guides Claude to check module status and install/upgrade before working with a client.

The overall vision is a growing set of MCP tools + skills for working with client settings and databases. This phase sets the pattern: **tools enforce, skills guide**.

## Goals

1. The `sm` SQL scripts live in **this repo** (`github.com/neemevool/settingmanager`), versioned together with the C# that calls them. Work done here always updates them here.
2. The scripts ship as plain files inside the MCP Docker image.
3. A single module version number defines "available" (shipped) vs "installed" (in `sm.moduleinfo`).
4. New MCP tool `install_module(client)` installs or upgrades `sm` using only the shipped scripts.
5. `module_status(client)` reports installed version, available version and a state.
6. Every other MCP tool checks the module first and returns a structured error — never an exception/server fault — when the state is not `up_to_date`.
7. MCP server instructions (sent on connect) tell Claude to check `module_status` first.
8. A Claude Code plugin in this repo bundles the skill(s) and the MCP connection config; users install it with `/plugin marketplace add` + `/plugin install`.
9. `composeprod.yaml` gains the `vtmf-settings-mcp` service.

## Non-goals

- The Settings UI (VTMF.Settings) does **not** install or upgrade `sm`. It stays as it is. Install is MCP-only for now.
- No changes to `VTMF.SQLInstaller` behaviour, its per-file versioning (`versions.csv` / TFS), or `VTMF.SQLInstaller/config/install.xml`. (Removing the stale `settingsmanagement` set from `VTMF.Settings/install.xml` is in scope — see §8.)
- No numbered/forward-only migration framework. Scripts stay idempotent; upgrade = re-run all.
- No write tools for settings (that is phase 2).
- No downgrade support.

## Anti-goals (failure even if it "works")

- A tool returns data from an outdated `sm` module. Outdated modules may contain serious bugs; that is the reason upgrades exist. Read tools must **refuse**, not warn.
- `install_module` accepts any SQL text, file path, or script selection from the caller. It may only run the scripts shipped with the tool.
- A script change reaches the repo without a version bump, so existing clients never get flagged as outdated.
- A failed upgrade leaves the module half old / half new while reporting a version.
- Any MCP tool crashes (unhandled exception) because `sm` is missing or broken.

## Decisions made by the user

- **Script location:** move into this repo; ship as files in the Docker image. The database5 set was more complex than needed.
- **Who installs:** MCP only.
- **Safety model:** the tool itself is the safeguard — it can only run the shipped scripts. No confirmation step is needed; if Claude decides to install, it may. Malicious script content is out of scope (repo owner's responsibility).
- **Outdated module:** all tools except `module_status` and `install_module` refuse with `module_outdated` and do not run.
- **Skill distribution:** the repo is on GitHub (private; users who need it have access) → distribute as a Claude Code plugin from this repo.
- **Prod compose:** add `vtmf-settings-mcp` to `composeprod.yaml` in this phase.
- **Permissions:** the existing per-client logins have full access (they already install other assets). No separate install credential.
- **`InstallMode`:** legacy; `install_module` ignores it.
- **Hosting:** each user runs the containers on their own machine. No authentication in this phase; web hosting would come with a full authorization layer later.
- **database5 copy:** `database5\settingsmanagement` is removed by the user (outside this repo, not part of the build work). This repo becomes the only source.

## Design

### 1. SQL source in this repo

- Location (builder may adjust): `VTMF.Settings.Core/Sql/settingsmanagement/`, keeping the `###-type-name.txt` naming and ordinal file-name ordering.
- Starting content (copy before the user deletes the database5 folder): the current files except `999-script-moduleversion.txt` from `C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5\settingsmanagement\`, copied as-is (preserve encoding handling — some database5 files are UTF-16 with BOM; normalising to UTF-8 is fine).
- Files are included in the Core project as content copied to output (`CopyToOutputDirectory`), so they land in the publish folder of every app that references Core — including the MCP image — without Dockerfile path juggling. Builder may choose another packaging as long as the result is **plain files on disk in the image** under a fixed path relative to the app base directory.
- All scripts remain idempotent (`IF … IS NULL`, `CREATE OR ALTER`). Destructive changes (drop/reshape a table) must still be written idempotently and safely inside the scripts.
- No grants to role `[worker]` (unchanged rule from phase 1).

### 2. Module version — single source of truth

*My call:* the available version is one integer stored in a file in the script folder (e.g. `version.txt`). The `999-script-moduleversion.txt` script is removed; the installer writes `sm.moduleinfo` itself after all scripts succeed, using the version from that file. This avoids keeping a literal in a SQL script and a constant in C# in sync.

- The `sm.moduleinfo` table and `sm.GetModuleInfo` procedure stay as in phase 1. The first version shipped from this repo is **2** (database5's copy stamped 1), so any database installed from database5 shows as `outdated` and gets upgraded to the repo version.
- *My call — bump guard:* a unit test computes a hash over all script files (names + normalised content) and compares it with a hash recorded next to the version (e.g. `version.txt` holds `version` and `hash`, or a separate `scripts.sha256`). If scripts changed but the recorded hash/version did not, the test fails with a message telling the developer to bump the version and update the hash. This makes "always updated by the work we do here" enforceable by the gate.
- A note is added to the repo `CLAUDE.md`: any change under the `sm` script folder requires a version bump.

### 3. Module state

`SmClient.GetModuleStatusAsync` is extended to return:

| Field | Meaning |
|---|---|
| `Installed` | bool (existing) |
| `Version` | installed version or null (existing) |
| `AvailableVersion` | version shipped with this tool |
| `State` | `not_installed` \| `up_to_date` \| `outdated` \| `newer_than_tool` |

`newer_than_tool` = installed version > available version (a newer image was used elsewhere).

### 4. Module gate for all tools

- Every `SmClient` operation other than status/install checks the module state before running (builder chooses the mechanism — e.g. a cached-per-call check in `SmClient`, or a guard in `ToolResult.RunAsync`; the check must not be skippable by a new tool forgetting to call it — prefer a central place).
- Error codes (added to `SmErrorCodes`), surfaced as `{ ok:false, error:{ code, message } }`:
  - `module_not_installed` (exists) — message: run `install_module`.
  - `module_outdated` (new) — includes installed and available versions; message: run `install_module`.
  - `module_newer_than_tool` (new, *my call*) — tools refuse; message: update the MCP server image.
- Existing message in `SmException` ("Install set 'settingsmanagement' with the VTMF.Settings installer") is updated to point at `install_module`.
- Unexpected SQL errors must not escape a tool. Phase 1 does **not** fully do this today (`SmException.TryTranslate` handles only 208/2812 on `sm.` objects and 51000–51999; `ToolResult.RunAsync` catches only `SmException`/`KeyNotFoundException`). *My call:* `ToolResult.RunAsync` also catches `SqlException` (incl. connection failures) and returns `sm_error` with the SQL message.
- Performance: one lightweight `sm.GetModuleInfo` call per tool invocation is acceptable. Caching across calls is not required and, if added, must not hide an upgrade or downgrade done by another instance.

### 5. `install_module(client)` tool

- Input: `client` only. No SQL, path, or script-selection parameters.
- Behaviour:
  1. Resolve client (unknown → `unknown_client`). *My call:* the metadata database entry (`DefaultMetaBase`, returned by `list_clients`) is not an install target; `install_module` refuses it with `unknown_client`.
  2. Read current state. If `newer_than_tool` → refuse with `module_newer_than_tool` (never downgrade). If `up_to_date` → re-running is allowed and harmless (*my call*: re-run anyway, so the tool can repair a damaged install at the same version; returns `before == after`).
  3. Take an exclusive application lock (`sp_getapplock`, resource e.g. `sm.install`) so two concurrent installs on the same database serialise (*my call*).
  4. Run every shipped script in ordinal file-name order, split into batches on `GO` using the existing `VTMF.SQLInstaller.Services.SqlScriptParser`.
  5. *My call:* run all batches in a **single transaction**; on any failure roll back and return `install_failed` with the failing script file name and SQL error message. On success, upsert `sm.moduleinfo` (module `settingsmanagement`, version = available, installed = now) inside the same transaction, then commit. If some statement in the scripts cannot run inside a transaction, the builder must restructure that script rather than drop atomicity.
- Output: `{ client, previousState, previousVersion, installedVersion, scriptsRun }`.
- Permissions: uses the existing client connection (`SqlConnectionProvider`); those logins have full access. If permission is ever missing it surfaces as `install_failed` with the SQL error text. `SqlConnectionParameters.InstallMode` is ignored.
- Prerequisites: `sm` depends on base objects (e.g. `dbo.processtypes`, `dbo.ServerProcessSettings`, settings functions). Missing prerequisites surface as `install_failed` naming the failing script. A dedicated prerequisite check is not required.

### 6. MCP server instructions

Set the server's instructions (ModelContextProtocol C# SDK server options; builder verifies the exact API) for both HTTP and stdio transports. Content, roughly:

> Before working with a client, call `module_status`. If the state is `not_installed` or `outdated`, call `install_module` for that client, then continue. If the state is `newer_than_tool`, stop and tell the user the MCP server image must be updated. Tool errors are returned as `{ ok:false, error:{ code } }`.

This is the baseline that travels with the image and works without the plugin.

### 7. Claude Code plugin & skill

- This repo becomes a plugin marketplace: `.claude-plugin/marketplace.json` at repo root listing one plugin, e.g. `vtmf-settings`, whose files live in a folder such as `plugins/vtmf-settings/` (builder verifies the current plugin/marketplace layout against Claude Code docs).
- Plugin contents:
  - `.mcp.json` connecting to the MCP server over HTTP, default `http://localhost:8081/mcp`. *My call:* allow override via an environment variable with default (if Claude Code's `.mcp.json` env expansion supports defaults; otherwise document how to change it).
  - Skill `sm-module` (name is builder's call): when to use (before any work with a client's settings via the `vtmf-settings` tools); steps: `list_clients` if client unknown → `module_status` → if `not_installed`/`outdated` run `install_module` and report previous → new version → if `newer_than_tool` stop and tell user to update the image → on `install_failed` show the failing script and SQL error and stop. Also: how to interpret `module_*` error codes returned by any tool.
- Plugin has a version; bump it when skills change.
- User install instructions go into `VTMF.Settings/README.md`: `/plugin marketplace add neemevool/settingmanager` (private repo — user's git credentials must have access) then `/plugin install vtmf-settings@<marketplace-name>`; updating via `/plugin update` / marketplace update.
- Skill/image drift is acceptable: the tools enforce correctness, a stale skill can only give weaker guidance.

### 8. Docker & compose

- `Dockerfile.mcp`: ensure the script files are present in the runtime image (automatic if they are Core content files in publish output; verify).
- `composeprod.yaml`: add
  ```yaml
  vtmf-settings-mcp:
    image: vtmf/settingsmanager-mcp:v2.16   # tag scheme: builder's call, match the UI image tag
    ports:
      - "127.0.0.1:8081:8080"   # my call: local-only, the server has no auth
    env_file: settings.env
  ```
- The UI `Dockerfile` needs no change. The stale `settingsmanagement` set in `VTMF.Settings/install.xml` (points at `$/Sawmill/database5/settingsmanagement`) is removed (*my call*: install is MCP-only and the source moved).

### 9. Tests

- `SqlServerFixture` installs `sm` into the test database through the **new installer** (dogfooding) instead of reading `database5\settingsmanagement`. Base prerequisite scripts may still come from `DATABASE5_PATH`.
- New tests (Testcontainers SQL Server, as in phase 1):
  - status on empty DB → `not_installed`; after install → `up_to_date` with available version.
  - install on DB stamped with lower version → upgrades, state `up_to_date`, `previousVersion` reported.
  - DB stamped higher than available → status `newer_than_tool`; `install_module` refuses; read tools refuse with `module_newer_than_tool`.
  - DB stamped lower → every read tool refuses with `module_outdated` (cover all existing tools, e.g. via a theory over tool invocations).
  - `not_installed` → every read tool returns `module_not_installed`, no exception.
  - failing script (e.g. DB missing a prerequisite) → `install_failed` naming the script; no `sm.moduleinfo` row written / previous version unchanged (rollback).
  - re-run at same version succeeds.
  - bump-guard hash test (section 2).
- Gate: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` must pass.

## Left to the builder

- Exact folder names, file names for the version/hash manifest, skill name, plugin name.
- Mechanism of the central module gate, as long as no tool can bypass it.
- Exact MCP SDK API for server instructions and tool registration.
- Image tag scheme for the MCP image.
- Output shape details beyond the fields listed.
