# Settings Management Tools — Design

**Date:** 2026-10-16 · **Status:** Approved in brainstorming, pending written-spec review
**Background reference:** `docs/settings-architecture.md` (v1.1)

## 1. Goal

Give humans and coding agents **one identical, safe way** to read and change VTMF client settings. All rules
live in stored procedures in a new `sm` schema deployed to every client DB. VTMF.Settings (UI) and a new MCP server
are thin clients over these procedures, sharing an extracted Core library.

Use cases served (database side):

1. Apply a behaviour (feature) to a client process.
2. Clone a process's settings from client Y to client Z.
3. Inspect which settings exist / are used / are configured (backend catalogue), as input for customizability checks.

## 2. Decisions

| # | Decision |
|---|---|
| D1 | Hybrid DB-first architecture: logic in `sm.*` procedures; UI and MCP are thin clients |
| D2 | MCP server is a separate project (`VTMF.Settings.Mcp`), runnable as console (stdio) or in Docker Compose (HTTP) |
| D3 | Shared `VTMF.Settings.Core` library: Entities + Services extracted from VTMF.Settings, plus `SmClient` / `SmOrchestrator` |
| D4 | Dedicated schema `sm` (keeps management procedures out of WS's `dbo.Get{infotype}Info` reach) |
| D5 | Plans may be applied by the agent after showing the plan; gate = MCP tool permission prompt + plan checksum |
| D6 | Feature model redesigned as "feature = named desired state with parameters"; one plan engine for all writes |
| D7 | Canonical environment layer = separate table `{table}{environment}`; environment rows in the base table are legacy |
| D8 | Supported floor SQL Server 2019 (compatibility level 150): customers run older servers, so no `json` type or `REGEXP_*`; definition parsing runs in `SmClient` (.NET regex) |
| D9 | No authentication in v1 (local use is intentional); modification identity = SQL login of the connection |
| D10 | SPA settings catalogue is a separate tool/subagent — out of scope |

## 3. Architecture

```
VTMF.Settings.sln
├─ VTMF.Settings.Core      class library
│    Entities/, Services/ (extracted), SqlConnectionProvider, SqlConnectionParameters
│    SmClient (typed wrappers over sm.*), SmOrchestrator (cross-DB operations), DTOs
├─ VTMF.Settings           Blazor UI → references Core (behaviour unchanged in phases 1–3)
├─ VTMF.Settings.Mcp       console app → references Core; ModelContextProtocol SDK
└─ VTMF.Settings.Tests     xUnit + Testcontainers (SQL Server 2019)

database5/settingsmanagement/            install.xml set "settingsmanagement" (client DBs)
database5/settingsmanagement/metabase/   install.xml set "settingsmetabase" (metabase DB, phase 3)
```

Responsibilities:

- **`sm` (client DB):** layer mapping, alias/override detection, diff, ordering, checksum, transactional apply,
  rollback, catalogue scan. The only place where rules live.
- **Core:** connections, procedure calls, JSON (de)serialization, cross-DB orchestration (clone, compare, feature
  expansion, harvest).
- **MCP:** exposes Core operations as tools; no business rules.
- **UI:** unchanged until phase 4.

### 3.1 Project build requirements

`VTMF.Settings.Core` and `VTMF.Settings.Mcp` `.csproj` must set:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<AnalysisMode>Recommended</AnalysisMode>
<AnalysisModePerformance>All</AnalysisModePerformance>
<AnalysisModeSecurity>All</AnalysisModeSecurity>
```

Target framework: `net10.0` (same as VTMF.Settings, which references Core).
Consequence: code extracted from VTMF.Settings into Core must be brought to zero warnings under these rules. Where a
rule cannot reasonably be satisfied (e.g. CA2100 for validated dynamic table names), suppress it narrowly with
`[SuppressMessage(..., Justification = "...")]` at the member level — no project-wide `NoWarn`.

## 4. Row model and desired state

### 4.1 Managed tables (v1)

- Backend: `ServerProcessSettings` rows `{process}.*` (prefix stripped in API, re-added by the engine).
- Frontend: `{process}{View}` tables and overlays `{table}{env}`, `{table}{lang}`.
- Global `settings`: out of scope (model compatible).

### 4.2 Layers

| Layer | Physical table | `workercode` |
|---|---|---|
| `base` | `{table}` | `ALL` |
| `env:{E}` | `{table}{E}` | `{E}` (as read by WS `LayoutListSettingReader`) |
| `lang:{L}` | `{table}{L}` | `ALL` |

For `ServerProcessSettings` only `base` is supported in v1.

### 4.3 Row shape (API)

```json
{ "table": "DocumentItemsListLayout", "layer": "base",
  "name": "Column.QTY.Caption", "value": "Kogus",
  "settingtype": "Setting", "listvalue": null, "ordernr": 120 }
```

`table` = view suffix, or `ServerProcessSettings`.

### 4.4 Row identity

- `Setting` rows: (physical table, workercode, NAME).
- `SettingGroup` rows: (physical table, workercode, NAME, VALUE); name-keyed groups (`Detail.*`, same rule as
  `SettingTreeNode.IsNameKeyedGroup`) by NAME.

### 4.5 Desired state

```json
{ "process": "spaorderassembling",
  "mode": "merge",
  "scope": [ { "table": "DocumentItemsListLayout", "layer": "base" } ],
  "rows": [ { "table": "...", "layer": "base", "name": "...", "value": "...",
              "settingtype": "Setting", "listvalue": null, "ordernr": null,
              "op": "upsert", "after": "Column=LOT" } ] }
```

- `merge`: upsert listed rows; delete only rows with `op: "remove"`.
- `replace`: listed rows are the complete content of every (table, layer) in `scope`; absent rows are deleted;
  anything outside `scope` is untouched.

### 4.6 Ordering

- `replace`: `ordernr` taken from desired rows.
- `merge`, new row without `ordernr`: after `after` anchor (`NAME` or `NAME=VALUE`) if given; else after the last row
  sharing the longest common name prefix; else table end.
- No gap available → renumber the table in steps of 10; renumbering appears as plan items.
- `ordernr` change on a `SettingGroup` row is an update.

### 4.7 Guardrails

| Code | Condition | Effect |
|---|---|---|
| `SERVERSETTING` | Row with `settingtype='ServerSetting'` targeted, or existing one would be touched | Error for explicit rows; skipped silently in replace scope; values masked in all reads |
| `WORKER_LAYER` | Row targets a real worker code | Error; message suggests env / lang layer or process-type override |
| `OVERRIDE_PROC` | Active (non-dash) `Get{process}{View}Info` exists for a target table | Warning |
| `ALIAS_TARGET` | Process has `SETTINGPROCEDURES` alias and no own table | Error unless option `targetAlias=true`; plan lists affected variants |
| `TABLE_MISSING` | Physical table does not exist | Error unless option `createTable=true` (create item in plan, standard template) |
| `LEGACY_ENV` | Base table contains rows with `workercode` = an environment name | Warning; migration plan available |

## 5. `sm` procedures (client DB)

`sm` scripts target SQL Server 2019 (compatibility level 150); no 2022+ syntax.

Location: `database5/settingsmanagement/`, `###-type-name.txt` convention, idempotent, depends only on set `base`.
Module version recorded in `sm.moduleinfo`. Errors: `THROW` with stable numbers in range 51000–51999.

### 5.1 Read (phase 1)

| Procedure | Returns |
|---|---|
| `sm.GetModuleInfo` | Installed module version |
| `sm.GetProcesses` | `processtypes` rows, aliases, variants (reverse alias), settings tables per process |
| `sm.GetTables @process` | Physical tables per view and layer, row counts, overriding `Get…Info` procedure (active/dash) |
| `sm.GetSettings @process, @table, @layer, @namePrefix` | Raw rows in API shape; `ServerSetting` values masked |
| `sm.GetEffectiveSettings @process, @table, @environment, @language` | Effective rows with source layer; frontend replicates WS `LayoutListSettingReader` (incl. alias table candidates), backend replicates `GetProcessSettingValues` |
| `sm.GetSettingsUsage @process, @namePrefix` | Backend catalogue sources: in-scope processes, candidate `sys.sql_modules` definitions and masked `ServerProcessSettings` rows. `SmClient` scans the definitions (.NET regex) for `GetPSV*`, `GetProcessSettingValue*` and direct table reads, and builds one row per (setting, procedure) with call style (`psv`/`legacy`/`direct`/`unparsed`), default in code and configured value per layer, plus configured-but-unused settings |
| `sm.GetLegacyEnvRows @process` | Base-table rows whose `workercode` is an environment name |

### 5.2 Plan engine (phase 2)

Tables:

- `sm.plan`: `id`, `created`, `createdby` (`SUSER_SNAME()`), `process`, `description`, `source`
  (`manual` | `clone:{client}` | `feature:{name}@{version}` | `migration` | `rollback:{planid}`), `mode`,
  `desired` (`nvarchar(max)`, validated with `ISJSON`), `checksum`, `status` (`Created` | `Applied` | `Failed` | `Stale` | `RolledBack`),
  `appliedat`, `appliedby`, `message`.
- `sm.planitem`: `planid`, `seq`, `physicaltable`, `workercode`, `action` (`CreateTable` | `Insert` | `Update` |
  `Delete`), after-image and before-image of NAME, VALUE, settingtype, LISTVALUE, ordernr.
- `sm.planwarning`: `planid`, `severity`, `code`, `message`.

Procedures:

- `sm.CreatePlan @desired, @description, @source, @options` (JSON as `nvarchar(max)`, validated with `ISJSON`) → validate, guardrails, diff (incl.
  renumbering), store; returns plan id, checksum, items, warnings. Errors → nothing stored. Empty diff → "no
  changes", no plan stored.
- `sm.ApplyPlan @planid, @checksum` → single `SERIALIZABLE` transaction: verify checksum and status `Created`; verify
  every before-image matches the current row (else `Stale`, no change); apply items in `seq` order; `Applied`. Any
  error → rollback, `Failed` with message.
- `sm.CreateRollbackPlan @planid` → inverse plan from before-images (applied via `ApplyPlan`); original marked
  `RolledBack` when the rollback plan is applied.
- `sm.CreateLegacyEnvMigrationPlan @process` → moves legacy env rows into overlay tables.
- `sm.GetPlans @process, @top`, `sm.GetPlan @planid` → history and audit.

Checksum: `HASHBYTES('SHA2_256', <canonical JSON of plan items ordered by seq>)`, hex-encoded.
The plan tables are the audit trail and backup for engine changes (replacing `bck_*` / `settingslog` for these).

## 6. Core library

- Extract `Entities/`, `Services/`, `SqlConnectionProvider`, `SqlConnectionParameters` from VTMF.Settings; keep
  namespaces so the UI changes only references. UI-model dependencies of services (`SettingTreeNode`,
  `NewSettingArgs`, `FeatureAddResult`, …) move into Core as plain models, or the dependent methods stay in the UI
  project — decided per method during extraction.
- `SmClient(SqlConnectionProvider)`: async method per `sm.*` procedure; DTOs for rows, desired state, plans,
  warnings; `SmException` (error number, message) for procedure errors.
- `SmOrchestrator`:
  - `CompareClients(source, target, process, scope)` → diff without storing.
  - `ClonePlan(source, target, process, scope)` → read source via `sm.GetSettings`, build replace-mode desired state,
    `CreatePlan` on target with `source=clone:{source}`.
  - Phase 3: `FeaturePlan`, `HarvestDraft`.
- `SmModuleStatus(client)` → installed / version / missing.

## 7. MCP server

- Generic Host, `ModelContextProtocol` SDK. `--transport stdio` (default) or `--transport http` (Docker; URL via
  `--urls` / `ASPNETCORE_URLS`).
- Configuration identical to VTMF.Settings (`SqlConnectionParameters` via env vars / User Secrets / `settings.env`).
  Added to `compose.yaml` as a second service.
- Tools (all take `client`; return compact JSON):
  - Read: `list_clients`, `module_status`, `list_processes`, `list_tables`, `get_settings`,
    `get_effective_settings`, `settings_usage`, `legacy_env_rows`, `list_plans`, `get_plan`.
  - Plan: `create_plan`, `clone_plan`, `compare_clients`, `rollback_plan`, `legacy_env_migration_plan`.
  - Apply: `apply_plan(client, planId, checksum)` — the only mutating tool.
  - Phase 3: `find_features`, `get_feature`, `feature_plan`, `harvest_draft`, `save_feature`.
- Tool descriptions state the rules: show the plan before applying; no worker-layer rows; SPA reload needed after
  apply; `ServerSetting` values are never available.
- Missing module on a client → `module_not_installed` error; never fall back to direct SQL.
- `SmException` → structured tool error result (not a server fault).

## 8. Features (phase 3)

Metabase DDL brought under version control in `database5/settingsmanagement/metabase/`.

- `sm.feature`: `id`, `name` (unique per version), `version`, `processtypename`, `description`, `tags`, `source`
  (`harvest:{client}/{process}` | `manual`), `createdby`, `created`.
- `sm.featurerow`: `featureid`, `seq`, `table`, `layer` (may be parameterized, e.g. `env:{{env}}`), `name`, `value`,
  `settingtype`, `listvalue`, `op`, `after`, `note`. Placeholders `{{param}}` allowed in `name`, `value`, `after`,
  `layer`.
- `sm.featureparam`: `featureid`, `name`, `description`, `default`, `valuelist`.
- `sm.featureset`, `sm.featuresetitem`: ordered features with parameter bindings (replaces `settingsfeaturegroup`).
- Procedures: `sm.FindFeatures @text, @process, @tags`, `sm.GetFeature`, `sm.SaveFeature @json` (always a new
  version), `sm.GetFeatureSet`.
- Migration script from `settingsfeature*`; non-mappable feature types reported for manual conversion; old tables
  left read-only until phase 4 completes.
- Install: `SmOrchestrator.FeaturePlan(client, feature|set, process, params)` → resolve params (missing → error),
  expand, `sm.CreatePlan` merge mode, `source=feature:{name}@{version}`.
- Harvest: `SmOrchestrator.HarvestDraft(client, process, baselineClient|none, namePrefixes)` → diff against baseline
  client or code defaults (`sm.GetSettingsUsage`); candidate rows grouped by prefix; saved via `SaveFeature` after
  selection, description and parameter marking.

## 9. Phase 4 — VTMF.Settings UI

- Settings Manager / Copy / Backup pages move to `SmClient`: edits become plans with preview; copy = clone + diff.
- Layer selector (base / env / lang).
- Plans page (history, details, rollback).
- Feature pages on the new metabase model.
- Remove direct-SQL paths as each page migrates.

## 10. Testing

- `VTMF.Settings.Tests` (xUnit; Testcontainers image `mcr.microsoft.com/mssql/server:2019-latest`, the supported floor).
- Fixture installs sets `base` + `settingsmanagement` scripts from `database5` and seeds a sample process with
  frontend tables, an alias variant, env/lang overlays, a `ServerSetting`, a legacy env row, and procedures with
  `GetPSV` / legacy reads.
- Coverage: diff semantics (merge/replace, identity rules), ordering and renumbering, every guardrail, apply,
  stale detection, failed-apply rollback, rollback plans, legacy env migration, effective-value parity with WS
  `LayoutListSettingReader` (golden cases), catalogue extraction.
- MCP: tool-level tests over `SmClient` against the same container.

## 11. Phasing

| Phase | Deliverables |
|---|---|
| 1 | Core extraction (with build requirements §3.1); `sm` module skeleton + read procedures + catalogue; `SmClient`; MCP server with read tools; Compose service; tests |
| 2 | Plan engine tables and procedures; `SmOrchestrator` clone/compare; MCP plan/apply tools; tests |
| 3 | Metabase DDL under VCS; feature model and procedures; migration; feature plan and harvest; MCP feature tools |
| 4 | UI migration to `SmClient`; layers; Plans page; feature pages; removal of direct SQL |

Each phase gets its own implementation plan.

## 12. Non-goals (v1)

Authentication; global `settings` table; SPA key catalogue; modifying business-logic procedures via tools;
changes to WS; migrating legacy `GetProcessSettingValue` code; UI approval workflow for plans.

## 13. Risks

- **Extraction under strict analyzers** may surface many warnings in existing services → done as a separate,
  required task (`plans/2026-10-16-core-services-migration.md`), not blocking phase 1; all findings fixed, narrow
  suppressions with justification only.
- **Effective-value parity** with WS depends on replicating `LayoutListSettingReader` including its known quirks
  (§10 of the architecture reference) → golden tests; quirks documented, not fixed in `sm`.
- **Catalogue regex** may miss unusual call shapes → report "unparsed" calls rather than drop them.
- **Module rollout** requires installing `settingsmanagement` per client before tools work.
