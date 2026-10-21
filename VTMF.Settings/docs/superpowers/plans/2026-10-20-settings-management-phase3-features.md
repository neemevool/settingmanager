# Settings management phase 3 — features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reusable features (description, rows with `{{placeholders}}`, typed parameters, post-apply steps) stored once in the metabase, created with a `create-feature` skill and applied to any customer process through the phase 2 plan engine with an `apply-feature` skill.

**Architecture:** A second SQL module `settingsmetabase` (schema `sm` in `DefaultMetaBase`) holds the feature tables and CRUD procedures; it reuses the client module's installer machinery and coexists with it in one DB (`sm.moduleinfo` keyed by module). Core gets `SmFeatureStore` (metabase), pure C# `SmFeatureValidator` / `SmFeatureExpander`, and `SmFeaturePlanner`, which expands a Finished feature into a merge desired state, calls `sm.CreatePlan` on the client and adds `SETTING_NOT_READ` warnings from the existing settings-usage scan. The client module (v6) adds `sm.GetAppliedFeatures`. A new MCP tool class exposes everything; two new plugin skills drive the conversations.

**Tech Stack:** .NET 10, Dapper, Microsoft.Data.SqlClient, System.Text.Json, ModelContextProtocol C# SDK, xUnit + Testcontainers.MsSql (SQL Server 2019), T-SQL (compat 150), Claude Code plugin skills.

**Spec:** `VTMF.Settings/docs/superpowers/specs/2026-10-20-settings-management-phase3-features-design.md` (builds on `2026-10-19-settings-management-phase2-plan-engine-design.md`; parent `2026-10-16-settings-management-tools-design.md`).

Paths are relative to the repo root. `<sm>` = `VTMF.Settings.Core/Sql/settingsmanagement`, `<meta>` = `VTMF.Settings.Core/Sql/settingsmetabase`, `<feat>` = `VTMF.Settings.Core/Sm/Features`.

## Global Constraints

- Build/test only via `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build|test|gate`; never `dotnet build`/`dotnet test`. Integration tests: `-Mode test -Filter "FullyQualifiedName~<Class>"` (Docker). Integration test classes use `[Trait("Category", "Integration")]` + `[Collection(SqlCollection.Name)]` with `(SqlServerFixture fixture)`; pure unit tests carry no trait so the gate runs them. `-Mode gate` must pass before finishing.
- Core/Mcp: `TreatWarningsAsErrors` + analyzers; only narrow `[SuppressMessage(..., Justification = ...)]`.
- SQL Server 2019 floor (compat 150): `OPENJSON`, `JSON_VALUE`, `STRING_AGG` allowed; no `json` type, no `REGEXP_*`, no 2022 syntax. Placeholder parsing happens in C# (parent D8).
- Scripts: `###-type-name.txt`, idempotent (`IF OBJECT_ID(...) IS NULL` for tables, `CREATE OR ALTER` for code), no top-level `BEGIN TRAN`; procedure bodies may use transactions.
- Client module `<sm>/module.json` `version` = **6** (Task 6). Metabase module `<meta>/module.json` `version` = **1** for the whole branch. Every script change updates the respective `sha256` (failing `RecordedHash_MatchesScripts*` prints it as two 32-char halves).
- Metabase objects must not collide with client-module object names (both live in `sm` when they share a DB). Metabase procedure names all contain `Feature`.
- Identity `ORIGINAL_LOGIN()`; timestamps `datetime2(0)` UTC `SYSUTCDATETIME()`.
- Feature statuses (exact): `Draft`, `Finished`. Row `op`: `set` | `remove`. Row `settingtype`: `Setting` | `SettingGroup`. Param `type`: `Text` | `Number` | `Boolean` | `List`.
- Placeholder regex (exact): `\{\{([A-Za-z][A-Za-z0-9_]*)\}\}`, matched case-insensitively against param names. Built-ins (never asked, never declared): `processname`, `clientname`.
- Plan source for features (exact): `feature:{id}:{name}`.
- Metabase error numbers → codes (exact, add to `SmErrorCodes`): 52001 `feature_not_found`, 52002 `feature_invalid`, 52003 `feature_changed`, 52004 `feature_name_taken`, 52005 `feature_status`.
- Core-only codes (exact): `metabase_not_configured`, `feature_module_not_installed`, `feature_module_outdated`, `feature_module_newer_than_tool`, `feature_not_finished`, `feature_param_missing`, `feature_param_invalid`.
- Warning code (exact): `SETTING_NOT_READ` (severity `Warning`); finish/save validation warning `PARAM_UNUSED`.
- Plugin version `1.1.1` → `1.2.0`. No UI changes; `settingsfeature*` tables untouched.

## Review Focus

1. **A placeholder answer that makes an unsafe setting name** (e.g. `name` = `LOT'; DROP`) — `sm.CreatePlan`'s name validation refuses with a structured `invalid_name` error, nothing stored. Test in Task 7 (`FeaturePlan_UnsafeAnswer_IsRefusedByEngine`).
2. **Placeholder case and repetition** (`{{Name}}` in one row, `{{name}}` in five others, param declared `name`) — one question, every occurrence filled. Test in Task 5 (`Expand_PlaceholderCaseInsensitive_FillsAll`).
3. **Two people editing the same feature** — second save with the old revision gets `feature_changed`, first save kept. Test in Task 3 (`Save_StaleRevision_IsFeatureChanged`).
4. **Renaming to an existing name in different case** (`Barcode button` vs `barcode BUTTON`) — `feature_name_taken`. Test in Task 3 (`Save_NameTakenCaseInsensitive_IsRefused`).
5. **No `DefaultMetaBase` configured** — feature tools answer `metabase_not_configured`, not `unknown_client` or a crash. Test in Task 8 (`FeatureTools_NoMetabase_ReturnsMetabaseNotConfigured`).

---

### Task 1: Module-aware script sets, shared installer core, module-info filter

Make the script set carry its module name, let a second installer share the install routine, and stop the client status reading the wrong `sm.moduleinfo` row.

**Files:**
- Modify: `VTMF.Settings.Core/Sm/SmScriptSet.cs`
- Modify: `VTMF.Settings.Core/Sm/SmInstaller.cs`
- Create: `VTMF.Settings.Core/Sm/SmModuleInstall.cs`
- Modify: `VTMF.Settings.Core/Sm/SmClient.cs` (`GetModuleInfoAsync`)
- Test: `VTMF.Settings.Tests/Sm/SmScriptSetTests.cs`, `VTMF.Settings.Tests/Sm/SmModuleStatusTests.cs` (create, or add to the existing module status test class if one exists)

**Interfaces:**
- Produces:
  - `SmScriptSet.ModuleName` becomes an instance property `string ModuleName`; constants `SmScriptSet.ClientModule = "settingsmanagement"`, `SmScriptSet.MetabaseModule = "settingsmetabase"`.
  - `static SmScriptSet Load(string directory, string moduleName)`; `LoadDefault()` = client module (unchanged behaviour); `static SmScriptSet LoadMetabase()` = `Path.Combine(AppContext.BaseDirectory, "Sql", MetabaseModule)`.
  - `internal static class SmModuleInstall { Task<SmInstallResult> RunAsync(SqlConnection connection, string target, SmScriptSet scripts, CancellationToken ct) }` — lock, read version `WHERE module = scripts.ModuleName`, state check, batches, stamp with `scripts.ModuleName`, commit/rollback (moved verbatim from `SmInstaller`). `SmInstaller.InstallAsync` keeps its DefaultMetaBase guard and delegates.

- [ ] **Step 1: Write failing tests**
  - `Load_RecordsModuleName`: `SmScriptSet.LoadDefault().ModuleName == "settingsmanagement"`.
  - `GetModuleStatus_IgnoresOtherModuleRows` (integration, `test` client): insert `('aaamodule', 999)` into `sm.moduleinfo` (sorts before `settingsmanagement` on the clustered PK), assert `fixture.Sm.GetModuleStatusAsync(SqlServerFixture.Test).State == "up_to_date"`; delete the row in `finally`.
- [ ] **Step 2: Run, expect FAIL** — `-Mode test -Filter "FullyQualifiedName~SmScriptSetTests|FullyQualifiedName~SmModuleStatusTests"`. The status test reports `newer_than_tool` because `GetModuleInfoAsync` takes `rows[0]`.
- [ ] **Step 3: Implement** the interfaces above; `GetModuleInfoAsync` returns the row whose `Module` equals `scripts.ModuleName` (case-insensitive), else null. No SQL change.
- [ ] **Step 4: Run the same filter plus `FullyQualifiedName~SmInstaller`, expect PASS.**
- [ ] **Step 5: Commit** — `refactor(core): module-aware script sets and shared install routine`

---

### Task 2: Metabase module skeleton, installer, status gate, fixture

**Files:**
- Create: `<meta>/module.json`, `<meta>/001-script-schema.txt`, `<meta>/010-script-featuretables.txt`
- Create: `<feat>/SmFeatureInstaller.cs`, `<feat>/SmFeatureStore.cs` (status part only), `<feat>/SmFeatureModels.cs` (status DTO reuse only)
- Modify: `VTMF.Settings.Core/Sm/SmErrorCodes.cs`, `VTMF.Settings.Core/Sm/SmServiceCollectionExtensions.cs`
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs`
- Test: `VTMF.Settings.Tests/Sm/SmMetabaseModuleTests.cs`, `VTMF.Settings.Tests/Sm/SmScriptSetTests.cs`

**Schema (`010-script-featuretables.txt`), exact columns:**
- `sm.feature`: `id int IDENTITY PK`, `name nvarchar(100) NOT NULL`, `namekey AS UPPER(name) PERSISTED` with `UNIQUE` index `UX_feature_namekey`, `description nvarchar(max) NOT NULL DEFAULT N''`, `status nvarchar(10) NOT NULL DEFAULT 'Draft' CHECK (status IN ('Draft','Finished'))`, `builtonprocess nvarchar(100) NULL`, `createdby nvarchar(128) NOT NULL`, `created datetime2(0) NOT NULL`, `modifiedby nvarchar(128) NOT NULL`, `modified datetime2(0) NOT NULL`, `revision rowversion`.
- `sm.featurerow`: `featureid int FK ON DELETE CASCADE`, `seq int`, PK (`featureid`,`seq`), `tablekind nvarchar(100) NOT NULL`, `layer nvarchar(100) NOT NULL DEFAULT 'base'`, `name nvarchar(200) NOT NULL`, `value nvarchar(400) NULL`, `settingtype nvarchar(20) NOT NULL CHECK IN ('Setting','SettingGroup')`, `listvalue nvarchar(1600) NULL`, `op nvarchar(10) NOT NULL CHECK IN ('set','remove')`, `after nvarchar(600) NULL`, `note nvarchar(1000) NULL`.
- `sm.featureparam`: `featureid` FK cascade, `name nvarchar(50)`, PK (`featureid`,`name`), `seq int`, `description nvarchar(1000) NOT NULL DEFAULT N''`, `type nvarchar(10) CHECK IN ('Text','Number','Boolean','List')`, `defaultvalue nvarchar(400) NULL`.
- `sm.featureparamvalue`: `featureid`, `paramname`, `seq`, PK (`featureid`,`paramname`,`seq`), FK to `featureparam` cascade, `value nvarchar(400) NOT NULL`, `description nvarchar(1000) NOT NULL DEFAULT N''`.
- `sm.featurestep`: `featureid` FK cascade, `seq`, PK, `text nvarchar(max) NOT NULL`, `sql nvarchar(max) NULL`.
- `001-script-schema.txt`: create schema `sm` and `sm.moduleinfo` if missing, same DDL as `<sm>/001-script-schema.txt` (`module nvarchar(50) PK, version int, installed datetime2(0)`).

**Interfaces:**
- Consumes: Task 1 `SmScriptSet.LoadMetabase()`, `SmModuleInstall.RunAsync`.
- Produces:
  - `SmFeatureInstaller(SqlConnectionProvider connections, SmMetabaseScripts scripts, string connectionName = SqlConnectionParameters.DefaultMetaBaseClientName)` with `Task<SmInstallResult> InstallAsync(CancellationToken ct = default)`.
  - `sealed record SmMetabaseScripts(SmScriptSet Set)` — DI wrapper so both script sets are singletons; registered as `_ => new SmMetabaseScripts(SmScriptSet.LoadMetabase())`.
  - `SmFeatureStore(SqlConnectionProvider connections, SmMetabaseScripts scripts, string connectionName = DefaultMetaBaseClientName)`: `Task<SmModuleStatus> GetModuleStatusAsync(CancellationToken ct = default)` (reads `version FROM sm.moduleinfo WHERE module = 'settingsmetabase'` with a text query guarded by `OBJECT_ID('sm.moduleinfo')`); private gate `EnsureUpToDateAsync` throwing `feature_module_not_installed` / `feature_module_outdated` ("…call install_feature_module.") / `feature_module_newer_than_tool` (`SmMessages.NewerThanTool` text); private `OpenAsync` mapping `KeyNotFoundException` from `GetConnection` to `SmException(metabase_not_configured, "No DefaultMetaBase connection is configured; features need the shared metadata database.")`.
  - `SmErrorCodes`: the Core-only codes and 52001–52005 from Global Constraints; `FromSqlErrorNumber` maps 52001–52005. `SmException.TryTranslate` also translates 52000–52999.
  - Fixture: new DB `smmeta` and `smcoexist`; `DefaultMetaBase` → `smmeta`; client constant `Coexist = "coexist"` → `smcoexist` (base scripts + client install). Metabase module installed into `smmeta` during setup. Fixture exposes `MetabaseScripts`, `FeatureStore => new(Connections, MetabaseScripts)`, `FeatureInstaller`. Keep the existing "installer refuses DefaultMetaBase" test working (it checks the name, not the DB).

- [ ] **Step 1: Write failing tests** (`SmMetabaseModuleTests`, integration):
  - `FeatureModule_InstalledInMetabase_IsUpToDate`: `FeatureStore.GetModuleStatusAsync().State == "up_to_date"`, `Version == 1`.
  - `FeatureModule_Reinstall_IsIdempotent`: second `FeatureInstaller.InstallAsync()` succeeds, `PreviousVersion == 1`.
  - `FeatureModule_CoexistsWithClientModule`: `new SmFeatureInstaller(Connections, MetabaseScripts, SqlServerFixture.Coexist).InstallAsync()`; then client status for `coexist` is `up_to_date` (v6 after Task 6, so assert `== "up_to_date"` not a number) and `new SmFeatureStore(..., Coexist).GetModuleStatusAsync().State == "up_to_date"`.
  - `FeatureModule_NotInstalled_ReportsNotInstalled`: store on client `empty` → `State == "not_installed"`.
  - `SmScriptSetTests.RecordedHash_MatchesScripts_Metabase` (same chunked assert as the client test) and `LoadMetabase_ScriptsAreInOutputFolder` (count = scripts present after this task: 2; update in Task 3).
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~SmMetabaseModuleTests|FullyQualifiedName~SmScriptSetTests"`.
- [ ] **Step 3: Implement** scripts, `module.json` (`version: 1`), installer, store status/gate, error codes, DI registration (`SmMetabaseScripts`, `SmFeatureInstaller`, `SmFeatureStore`), fixture changes.
- [ ] **Step 4: Run, expect PASS** (record the printed metabase hash).
- [ ] **Step 5: Commit** — `feat(meta): settingsmetabase module v1 with feature tables and install`

---

### Task 3: Feature store procedures and `SmFeatureStore` CRUD

**Files:**
- Create: `<meta>/020-procedure-FindFeatures.txt`, `021-procedure-GetFeature.txt`, `022-procedure-SaveFeature.txt`, `023-procedure-SetFeatureStatus.txt`, `024-procedure-DeleteFeature.txt`
- Modify: `<meta>/module.json` (sha256), `<feat>/SmFeatureStore.cs`, `<feat>/SmFeatureModels.cs`
- Test: `VTMF.Settings.Tests/Sm/FeatureStoreTests.cs`

**API document (JSON, camelCase; row fields match `SmDesiredRow` spelling):**

```json
{ "id": 12, "name": "Barcode for articles without one", "description": "Allows adding a barcode …",
  "status": "Draft", "builtOnProcess": "sparelocateitems", "revision": 81234,
  "createdBy": "…", "created": "…", "modifiedBy": "…", "modified": "…",
  "rows":   [ { "table": "ProcessSettings", "layer": "base", "name": "Function.Barcode.Notfound.Action",
                "value": "{{notfoundAction}}", "settingtype": "Setting", "listvalue": null,
                "op": "set", "after": null, "note": null } ],
  "params": [ { "name": "notfoundAction", "description": "What happens when a scanned barcode is not found?",
                "type": "List", "default": "OptionsQuestion",
                "values": [ { "value": "OptionsQuestion", "description": "Ask the user to choose from options" } ] } ],
  "steps":  [ { "text": "Add the Directo route for SetBarcode", "sql": "IF NOT EXISTS (…) INSERT …" } ] }
```

`seq` = array position (1-based) for rows, params, values, steps; not part of the document. `revision` = `CONVERT(bigint, revision)`.

**Procedures:**
- `sm.FindFeatures @text nvarchar(200) = NULL, @status nvarchar(10) = N'Finished'` (`@status` NULL = all) → `id, name, description, status, builtonprocess, tablekinds (STRING_AGG DISTINCT tablekind, ', '), paramcount, modified`; `@text` `LIKE '%'+@text+'%'` over name, description, builtonprocess, any row name; order by name.
- `sm.GetFeature @id int = NULL, @name nvarchar(100) = NULL` → 5 result sets (feature incl. revision, rows, params, param values, steps) ordered by seq; none found → `THROW 52001`.
- `sm.SaveFeature @json nvarchar(max), @revision bigint = NULL` → `ISJSON` else 52002; no `id` → insert as `Draft`; with `id`: missing → 52001, `@revision` NULL or ≠ current → 52003; name taken by another feature (by `UPPER`) → 52004; in one transaction update header (`modifiedby/modified`) and delete+reinsert children from `OPENJSON`. Never changes `status`. Returns `id, revision`.
- `sm.SetFeatureStatus @id, @status, @revision` → 52001 / 52003 / invalid status 52002; sets status, modified. Returns `id, revision`.
- `sm.DeleteFeature @id, @revision` → 52001 / 52003; status `Finished` → `THROW 52005, N'Only draft features can be deleted; reopen it first.'`.

**Interfaces:**
- Produces (`<feat>/SmFeatureModels.cs`, `System.Text.Json` names as in the document): `SmFeature { int? Id; string Name; string Description; string Status; string? BuiltOnProcess; long? Revision; string? CreatedBy; DateTime? Created; string? ModifiedBy; DateTime? Modified; IReadOnlyList<SmFeatureRow> Rows; IReadOnlyList<SmFeatureParam> Params; IReadOnlyList<SmFeatureStep> Steps }`, `SmFeatureRow { Table; Layer="base"; Name; string? Value; SettingType="Setting"; string? ListValue; Op="set"; string? After; string? Note }`, `SmFeatureParam { Name; Description; Type; string? Default; IReadOnlyList<SmFeatureParamValue> Values }`, `SmFeatureParamValue { Value; Description }`, `SmFeatureStep { Text; string? Sql }`, `SmFeatureSummary { Id; Name; Description; Status; string? BuiltOnProcess; string TableKinds; int ParamCount; DateTime Modified }`, `SmFeatureSaveResult { int Id; long Revision; IReadOnlyList<SmPlanWarning> Warnings }`.
- `SmFeatureStore`: `FindFeaturesAsync(string? text, string? status = "Finished", ct)`, `GetFeatureAsync(int? id, string? name, ct)`, `SaveFeatureAsync(SmFeature feature, long? revision, ct)`, `SetFeatureStatusAsync(int id, string status, long revision, ct)`, `DeleteFeatureAsync(int id, long revision, ct)`. All gated. Validation hooks are added in Task 4.

- [ ] **Step 1: Write failing tests** (`FeatureStoreTests`, integration, unique names via `"t-" + Guid.NewGuid().ToString("N")[..8]`):
  - `Save_New_IsDraftAndRoundTrips` — rows/params/values/steps come back in order; a value `"Kogus õäöü"` round-trips.
  - `Save_Replace_ReplacesChildren` — second save with 1 row leaves exactly 1 row.
  - `Save_StaleRevision_IsFeatureChanged` (Review Focus 3).
  - `Save_NameTakenCaseInsensitive_IsRefused` (Review Focus 4) → `feature_name_taken`.
  - `Find_DefaultsToFinished_DraftsOnRequest` — draft absent by default, present with `status: "Draft"`; `text` matches a row name.
  - `SetStatus_FinishAndReopen` — status changes, revision changes.
  - `Delete_Finished_IsRefused` → `feature_status`; `Delete_Draft_Removes` → later `GetFeatureAsync` → `feature_not_found`.
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~FeatureStoreTests"`.
- [ ] **Step 3: Implement** procedures (update `LoadMetabase_ScriptsAreInOutputFolder` count to 7, update `sha256`) and store methods (`QueryMultiple` for `GetFeature`).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(meta): feature store procedures and SmFeatureStore`

---

### Task 4: Placeholders and validation (pure C#), wired into save and finish

**Files:**
- Create: `<feat>/FeaturePlaceholders.cs`, `<feat>/SmFeatureValidator.cs`
- Modify: `<feat>/SmFeatureStore.cs`
- Test: `VTMF.Settings.Tests/Features/SmFeatureValidatorTests.cs` (unit), `VTMF.Settings.Tests/Sm/FeatureStoreTests.cs` (two integration tests)

**Interfaces:**
- Produces:
  - `static class FeaturePlaceholders { IReadOnlySet<string> Find(string? text); string Substitute(string text, IReadOnlyDictionary<string,string> values); IReadOnlySet<string> BuiltIns }` — case-insensitive sets/dictionaries; `Substitute` leaves unknown placeholders untouched.
  - `static class SmFeatureValidator { SmFeatureValidation Validate(SmFeature feature, bool finish) }`, `record SmFeatureValidation(IReadOnlyList<string> Errors, IReadOnlyList<SmPlanWarning> Warnings)`.
- **Save rules (always):** name non-empty ≤ 100; each row: table non-empty and without `{{`, layer matches `^(base|env:.+|lang:.+)$` (placeholders allowed after the colon), op ∈ {set, remove}, settingtype ∈ {Setting, SettingGroup} (`ServerSetting` → error "ServerSetting rows cannot be part of a feature"), set rows have a value (may be empty string), remove rows ignore value; param names match `^[A-Za-z][A-Za-z0-9_]*$`, unique case-insensitively, not a built-in; type valid; List has ≥ 1 value.
- **Finish rules (`finish: true`, and on every save of a Finished feature):** description non-empty; ≥ 1 row or step; every non-built-in placeholder found in rows (name, value, listvalue, after, layer) and steps (text, sql) has a param; every param has a description; every List value has a description; a default, when present, is valid for its type (Number: `decimal.TryParse` invariant; Boolean: `True`/`False` case-insensitive; List: one of the values). Params used nowhere → warning `PARAM_UNUSED`.
- `SmFeatureStore.SaveFeatureAsync`: loads current status when `Id` is set; validates with `finish = (status == "Finished")`; errors → `SmException(feature_invalid, string.Join("; ", errors))` before calling SQL; returns warnings in `SmFeatureSaveResult`.
- `SmFeatureStore.SetFeatureStatusAsync(..., "Finished", ...)`: loads the feature, `Validate(finish: true)`, errors → `feature_invalid`; then calls the procedure.

- [ ] **Step 1: Write failing unit tests** (`SmFeatureValidatorTests`): `Find_IsCaseInsensitiveAndDistinct` (`"{{Name}}.{{name}}"` → 1), `Substitute_ReplacesAllOccurrences`, `Validate_UndeclaredPlaceholder_FailsFinishOnly` (save OK, finish error naming `name`), `Validate_ServerSettingRow_Fails`, `Validate_ListWithoutValues_Fails`, `Validate_InvalidDefaultForType_FailsFinish` (Number `"abc"`, Boolean `"yes"`, List value not listed), `Validate_BuiltInsNeedNoParam` (`{{processname}}`), `Validate_UnusedParam_Warns`, `Validate_PlaceholderInStepSql_NeedsParam`.
- [ ] **Step 2: Write failing integration tests**: `SetStatus_FinishInvalid_IsFeatureInvalid`; `Save_FinishedMadeInvalid_IsRefused`.
- [ ] **Step 3: Run, expect FAIL** — `-Filter "FullyQualifiedName~SmFeatureValidatorTests|FullyQualifiedName~FeatureStoreTests"`.
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run, expect PASS; commit** — `feat(core): feature placeholders and validation`

---

### Task 5: Feature expansion (pure C#)

**Files:**
- Create: `<feat>/SmFeatureExpander.cs`
- Test: `VTMF.Settings.Tests/Features/SmFeatureExpanderTests.cs` (unit)

**Interfaces:**
- Produces: `static class SmFeatureExpander { SmFeatureExpansion Expand(SmFeature feature, string process, string client, IReadOnlyDictionary<string,string>? answers) }`, `record SmFeatureExpansion(SmDesiredState Desired, IReadOnlyList<SmFeatureStep> Steps, IReadOnlyDictionary<string,string> Values)`.
- Behaviour:
  1. Values = built-ins (`processname` = process, `clientname` = client) + answers (case-insensitive keys; unknown keys ignored) + defaults for unanswered params. Params still without a value → `SmException(feature_param_missing, "Missing values for: a, b")`.
  2. Normalize/validate per type: Number `decimal.TryParse(InvariantCulture)` → keep the given text; Boolean `true/false` case-insensitive → `True`/`False`; List → must equal a listed value (case-insensitive, written in the listed spelling); Text any (empty allowed). Failures collected → `SmException(feature_param_invalid, "...")` naming param and allowed values.
  3. Desired state: `Process = process`, `Mode = "merge"`, `Scope = []`, rows mapped in seq order: `Table = row.Table`, `Layer/Name/Value/ListValue/After` substituted, `SettingType`, `Op = row.Op == "remove" ? "remove" : "upsert"`, `OrderNr = null`.
  4. Steps with text and sql substituted.

- [ ] **Step 1: Write failing tests**: `Expand_BarcodeExample_DependentNames` — rows `Function.Barcode.Notfound.Action={{notfoundAction}}`, `Function.{{notfoundAction}}.Notfound.ItemType=Option`, `Function.Barcode.Notfound.{{notfoundAction}}.Procedure=RowSelectionOptionsQuestion`, answer `optionsquestion` → names/values with `OptionsQuestion` (listed spelling); `Expand_PlaceholderCaseInsensitive_FillsAll` (Review Focus 2: `Column={{Name}}`, `Column.{{name}}.Caption`, …, answer key `NAME`); `Expand_UsesDefaults`; `Expand_Missing_Throws` (code `feature_param_missing`, message lists names); `Expand_InvalidTypes_Throw` (Number `"x"`, Boolean `"yes"`, List `"Other"`); `Expand_BooleanNormalized` (`"TRUE"` → `True`); `Expand_RemoveRow_MapsToRemove`; `Expand_LayerPlaceholder` (`env:{{env}}`); `Expand_BuiltIns` (`{{processname}}` in a step's sql); `Expand_IsMergeWithoutOrderNr`.
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~SmFeatureExpanderTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS; commit** — `feat(core): expand features into merge desired states`

---

### Task 6: Client module v6 — applied features

**Files:**
- Create: `<sm>/180-procedure-GetAppliedFeatures.txt`
- Modify: `<sm>/module.json` (`version: 6`, sha256), `VTMF.Settings.Core/Sm/SmClient.cs`, `VTMF.Settings.Core/Sm/SmModels.cs`
- Test: `VTMF.Settings.Tests/Sm/AppliedFeaturesTests.cs`, `VTMF.Settings.Tests/Sm/SmScriptSetTests.cs` (count 36 → 37)

**Procedure:** `sm.GetAppliedFeatures @process nvarchar(100) = NULL` → from `sm.[plan]` where `status = 'Applied'` and `source LIKE N'feature:%:%'` (and `process = @process` when given): latest plan per (featureid, process) via `ROW_NUMBER`: `featureid int` (= text between the first and second `:`; `TRY_CONVERT`, rows with NULL id skipped), `featurename` (text after the second `:`), `process`, `planid`, `appliedat`, `appliedby`. Order by `appliedat DESC`.

**Interfaces:**
- Produces: `SmAppliedFeature { int FeatureId; string FeatureName; string Process; int PlanId; DateTime AppliedAt; string AppliedBy }`; `SmClient.GetAppliedFeaturesAsync(string client, string? process = null, CancellationToken ct = default)` (gated).

- [ ] **Step 1: Write failing tests** (integration, client `plan`, sandbox process via `PlanSandbox.CreateProcessAsync`): `Applied_FeaturePlan_IsListed` — `CreatePlanAsync(..., source: "feature:42:Test feature")` + apply → listed with id 42, name, process; `RolledBack_IsExcluded` — rollback plan applied → not listed; `Manual_IsNotListed`.
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~AppliedFeaturesTests|FullyQualifiedName~SmScriptSetTests"`.
- [ ] **Step 3: Implement**, bump version, update sha256 and script count.
- [ ] **Step 4: Run, expect PASS** (also `FullyQualifiedName~SmMetabaseModuleTests` for the coexistence test).
- [ ] **Step 5: Commit** — `feat(sm): GetAppliedFeatures (module v6)`

---

### Task 7: `SmFeaturePlanner` — feature plan with backend usage check

**Files:**
- Create: `<feat>/SmFeaturePlanner.cs`
- Modify: `<feat>/SmFeatureModels.cs`, `VTMF.Settings.Core/Sm/SmServiceCollectionExtensions.cs`
- Test: `VTMF.Settings.Tests/Sm/FeaturePlannerTests.cs`

**Interfaces:**
- Consumes: `SmFeatureStore.GetFeatureAsync`, `SmFeatureExpander.Expand`, `SmClient.CreatePlanAsync`, `SmClient.GetSettingsUsageAsync`.
- Produces: `SmFeaturePlanner(SmClient sm, SmFeatureStore features)` with `Task<SmFeaturePlanResult> FeaturePlanAsync(string client, int featureId, string process, int sessionId, IReadOnlyDictionary<string,string>? parameters, SmPlanOptions? options = null, CancellationToken ct = default)`; `SmFeaturePlanResult { int FeatureId; string FeatureName; SmPlanResult Plan; IReadOnlyList<SmPlanWarning> Warnings; IReadOnlyList<SmFeatureStep> Steps; IReadOnlyDictionary<string,string> Values }`. Registered as singleton.
- Behaviour:
  1. Feature not `Finished` → `SmException(feature_not_finished, "Feature '{name}' is a draft; only finished features can be applied.")`.
  2. Expand; `CreatePlanAsync(client, desired, description: $"Feature {name}", sessionId, options, source: $"feature:{id}:{name}")`.
  3. Usage check only when the expanded state has `ServerProcessSettings` rows with `op = "upsert"`: one `GetSettingsUsageAsync(client, process)`; a setting is *read* when any usage row has the same `SettingName` (case-insensitive), `ProcedureName != null` and `!Unused`. Not read → `SmPlanWarning { Severity = "Warning", Code = "SETTING_NOT_READ", Message = $"'{name}' is not read by any {process} procedure; the code of {process} may not support this feature." }`; if any usage row has `CallStyle == "unparsed"`, append `" Some procedure reads could not be parsed, so this is uncertain."`.
  4. Steps and values from the expansion are returned as-is.

- [ ] **Step 1: Write failing tests** (integration; features saved to `smmeta` with unique names and finished; client `plan`; sandbox process; session via `StartSessionAsync`):
  - `FeaturePlan_SameFeatureTwoProcesses` — a `DocumentListLayout` row with `{{name}}` planned for two sandbox processes → each plan's items target `{p}DocumentListLayout`; plan `Status == "Created"`.
  - `FeaturePlan_SourceRecorded` — after `ApplyPlanAsync`, `GetAppliedFeaturesAsync` lists the feature id/name.
  - `FeaturePlan_Draft_IsRefused` → `feature_not_finished`.
  - `FeaturePlan_UnreadServerSetting_Warns` — `ServerProcessSettings` row `Feature.Unread` → `SETTING_NOT_READ`.
  - `FeaturePlan_ReadServerSetting_NoWarning` — create procedure `dbo.Get{p}FeatureProbe` whose body calls `dbo.GetProcessSettingValue('{p}', 'Feature.Read', ...)` in the same shape as the `GetPSV`/legacy calls in `VTMF.Settings.Tests/TestData/seed.sql`; row `Feature.Read` → no `SETTING_NOT_READ`.
  - `FeaturePlan_UnsafeAnswer_IsRefusedByEngine` (Review Focus 1) — Text param `name` = `"LOT'; DROP"` in a row name → `SmException` code `invalid_name`, no plan stored (`GetPlansAsync` count unchanged).
  - `FeaturePlan_StepsResolved` — step sql with `{{processname}}` comes back with the process name.
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~FeaturePlannerTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS; commit** — `feat(core): SmFeaturePlanner with backend usage warnings`

---

### Task 8: MCP feature tools

**Files:**
- Create: `VTMF.Settings.Mcp/Tools/SettingsFeatureTools.cs`
- Modify: `VTMF.Settings.Mcp/McpServerSetup.cs` (register + `Instructions`)
- Test: `VTMF.Settings.Tests/Mcp/SettingsFeatureToolsTests.cs`, `VTMF.Settings.Tests/Mcp/McpServerSetupTests.cs`

**Interfaces:**
- Consumes: `SmFeatureStore`, `SmFeatureInstaller`, `SmFeaturePlanner`, `SmClient.GetAppliedFeaturesAsync`.
- Produces: `[McpServerToolType] public sealed class SettingsFeatureTools(SmFeatureStore features, SmFeatureInstaller installer, SmFeaturePlanner planner, SmClient sm)`, every tool wrapped in `ToolResult.RunAsync`:

| Tool | Signature (after `CancellationToken` omitted) | Annotations |
|---|---|---|
| `feature_module_status` | `()` → `SmModuleStatus` | ReadOnly |
| `install_feature_module` | `()` → `SmInstallResult` | Destructive, Idempotent |
| `find_features` | `(string? text, string? status)` → `IReadOnlyList<SmFeatureSummary>`; `status` default `"Finished"`, `"all"` → null | ReadOnly |
| `get_feature` | `(int? id, string? name)` → `SmFeature` | ReadOnly |
| `save_feature` | `(SmFeature feature, long? revision)` → `SmFeatureSaveResult` | not ReadOnly, not Destructive |
| `set_feature_status` | `(int id, string status, long revision)` → `SmFeatureSaveResult` | not ReadOnly |
| `delete_feature` | `(int id, long revision)` → `bool` (true) | Destructive |
| `feature_plan` | `(string client, int featureId, string process, int sessionId, Dictionary<string,string>? parameters, SmPlanOptions? options)` → `SmFeaturePlanResult` | not ReadOnly, not Destructive |
| `list_applied_features` | `(string client, string? process)` → `IReadOnlyList<SmAppliedFeatureInfo>` | ReadOnly |

  `SmAppliedFeatureInfo` (Mcp or Core models) = `SmAppliedFeature` + `string? Description` + `string? CurrentName` + `string? Status` from `FindFeaturesAsync(null, null)` joined on id (null when the feature no longer exists).
- Descriptions state (exact intent): only Finished features can be planned; show the plan to the user before `apply_plan`; post-apply SQL is shown to the user to run and never executed by the assistant; descriptions say what the feature enables for the end user; `save_feature` replaces the whole feature and needs the last `revision`.
- `Instructions` gains: "Feature tools use the shared metadata database: if a feature tool returns feature_module_not_installed or feature_module_outdated, call install_feature_module, then continue; feature_module_newer_than_tool means the MCP server image must be updated."

- [ ] **Step 1: Write failing tests** (`SettingsFeatureToolsTests`, integration): `FeatureTools_SaveFindGetFinishPlan_Flow` (save → find drafts → finish → `feature_plan` on `plan` sandbox → `Ok`, items present); `FeatureTools_Errors_AreEnvelopes` (`get_feature(id: -1)` → `feature_not_found`; `feature_plan` with missing param → `feature_param_missing`); `FeatureTools_NoMetabase_ReturnsMetabaseNotConfigured` (Review Focus 5: build `SqlConnectionProvider` without `DefaultMetaBase` from the fixture's parameters, `find_features` → `metabase_not_configured`); `ListAppliedFeatures_JoinsNames`. `McpServerSetupTests`: the nine tool names are registered.
- [ ] **Step 2: Run, expect FAIL** — `-Filter "FullyQualifiedName~SettingsFeatureToolsTests|FullyQualifiedName~McpServerSetupTests"`.
- [ ] **Step 3: Implement.** If the SDK cannot bind `Dictionary<string,string>` or `SmFeature`, check how `create_plan` binds `SmDesiredState` and follow it.
- [ ] **Step 4: Run, expect PASS; commit** — `feat(mcp): feature tools`

---

### Task 9: Skills, plugin, README, docs, gate

**Files:**
- Create: `plugins/vtmf-settings/skills/create-feature/SKILL.md`, `plugins/vtmf-settings/skills/apply-feature/SKILL.md`
- Modify: `plugins/vtmf-settings/skills/manage-settings/SKILL.md` (step 10), `plugins/vtmf-settings/skills/sm-module/SKILL.md`, `plugins/vtmf-settings/.claude-plugin/plugin.json` (`1.2.0`), `README.md`, `CLAUDE.md`, `VTMF.Settings/CLAUDE.md` (domain model note), `VTMF.Settings/TASKHISTORY.md`

- [ ] **Step 1: `create-feature/SKILL.md`** — frontmatter `name: create-feature`, description: "Use when the user wants to define, record, package, edit, finish or list (unfinished) VTMF features, or says that settings they just changed should be reusable." Body = spec §7 steps 1–9 as instructions, including: describe first (bad/good example from the spec), propose 2–3 names, `save_feature` immediately as Draft and after every change (always pass the last `revision`; on `feature_changed` re-read and reapply), session → rows mapping (after-image → set, deleted → remove, `CreateTable` → note), placeholder marking with the barcode and column examples, parameter types (Boolean as True/False), post-apply steps with idempotent `IF NOT EXISTS` SQL, finish via `set_feature_status`, complexity rule, never edit settings tables from this skill (test via `manage-settings`, drafts are never applied).
- [ ] **Step 2: `apply-feature/SKILL.md`** — frontmatter `name: apply-feature`, description: "Use when the user wants some behaviour on a customer process ('could it be possible…', 'add … like on customer Y'), or names a VTMF feature to apply." Body = spec §8 steps 1–8: orient (`sm-module`, `feature_module_status`), interview, "like on Y" → `list_applied_features` on Y first, else `find_features`, else `compare_clients` + suggest `create-feature`; present matches by description; ask parameters in order (List values with descriptions, Boolean yes/no → True/False); session; `feature_plan` preview table + `SETTING_NOT_READ` explained; ask before `createTable`/`targetAlias`; `apply_plan` after confirmation; reload SPA; post-apply steps one by one, SQL shown for the consultant to run, never executed.
- [ ] **Step 3: Update** `manage-settings` step 10 → "…ask whether these changes form something reusable; if yes, continue with the `create-feature` skill, passing the client and session id." `sm-module`: add rows for `feature_module_not_installed`/`feature_module_outdated` → `install_feature_module`, `feature_module_newer_than_tool` → update MCP image, `metabase_not_configured` → configure `DefaultMetaBase`. Bump `plugin.json` to `1.2.0`.
- [ ] **Step 4: README** — tools list (nine feature tools); sections "Creating a feature" and "Applying a feature" with a short sample conversation each; sample prompts: "make this session a feature", "show unfinished features", "add the barcode button like on customer Y in sparelocateitems", "could the article get a barcode if it has none?"; MCP image update note (client module v6, metabase module v1).
- [ ] **Step 5: CLAUDE.md (root)** — add: any change under `VTMF.Settings.Core/Sql/settingsmetabase/` requires bumping its `module.json` version and `sha256` (same rule as the client module). `TASKHISTORY.md` — append the phase 3 prompts of this session and spec/plan paths.
- [ ] **Step 6: Gate** — `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` → PASS; also run the full integration suite once: `-Mode test -Filter "Category=Integration"` → PASS.
- [ ] **Step 7: Manual skill checklist** (record results in the PR/commit message): with the MCP server running against a dev client, "make this session a feature" triggers `create-feature` and saves a draft after the description step; "add the barcode button like on customer Y" triggers `apply-feature` and calls `list_applied_features` first; post-apply SQL is shown, not executed.
- [ ] **Step 8: Commit** — `feat(plugin): create-feature and apply-feature skills (plugin 1.2.0); docs`
