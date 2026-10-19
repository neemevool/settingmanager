# Settings management phase 2 — plan engine, sessions, `manage-settings` skill — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every settings change made through Claude Code becomes a stored, previewed, atomically applied and reversible plan inside a named session, driven by a skill so consultants never have to ask for a plan.

**Architecture:** New `sm` scripts (module v5) add `sm.session`, `sm.plan`, `sm.planitem`, `sm.planwarning` and procedures that diff a desired state into plan items (`sm.CreatePlan`), apply them in one serializable transaction (`sm.ApplyPlan`), and build inverse plans (single plan, session, legacy-env migration). `SmClient` wraps them; `SmOrchestrator` does cross-database clone/compare by calling `sm.CreatePlan` on the target (dry run for compare). A new MCP tool class exposes them; a new plugin skill `manage-settings` teaches the conversation flow; the root README documents it.

**Tech Stack:** .NET 10, Dapper, Microsoft.Data.SqlClient, System.Text.Json, ModelContextProtocol C# SDK, xUnit + Testcontainers.MsSql (SQL Server 2019), T-SQL (compat level 150), Claude Code plugin skills.

**Spec:** `VTMF.Settings/docs/superpowers/specs/2026-10-19-settings-management-phase2-plan-engine-design.md` (parent: `VTMF.Settings/docs/superpowers/specs/2026-10-16-settings-management-tools-design.md`, §4 row model, §5.2 plan engine)

Paths are relative to the repo root `C:\PROJECTS\VTMF\Settingmanager`. `<sm>` = `VTMF.Settings.Core/Sql/settingsmanagement`.

## Global Constraints

- Build/test only via `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build|test|gate`; never `dotnet build`/`dotnet test`. Integration tests need Docker: `-Mode test -Filter "FullyQualifiedName~<Class>"`. `-Mode gate` must pass before finishing.
- Core/Mcp keep `TreatWarningsAsErrors` + analyzers; only narrow `[SuppressMessage(..., Justification = ...)]`, no `NoWarn`.
- SQL Server 2019 floor (compat 150): `OPENJSON`, `JSON_VALUE`, `FOR JSON`, `STRING_AGG` allowed; no `json` type, no `REGEXP_*`, no 2022 syntax (`GREATEST`, `DATE_BUCKET`, `IS DISTINCT FROM`…).
- Scripts: `###-type-name.txt` in `<sm>`, idempotent (`IF OBJECT_ID(...) IS NULL` for tables, `CREATE OR ALTER` for code). No top-level `BEGIN TRAN`/`COMMIT` (the installer wraps all scripts in one transaction); transactions inside procedure bodies are allowed.
- `module.json` `version` = **5** for the whole branch (set in Task 1); every task that changes a script updates `sha256` (the failing `RecordedHash_MatchesScripts` prints it as two 32-char halves; concatenate). Update the script count in `SmScriptSetTests.LoadDefault_ScriptsAreInOutputFolder` whenever a script file is added.
- Dynamic SQL only with `QUOTENAME` on names validated by `sm.IsSafeName`; values always as `sp_executesql` parameters.
- Identity = `ORIGINAL_LOGIN()` for `createdby`/`appliedby`. Timestamps `datetime2` UTC (`SYSUTCDATETIME()`).
- Row identity (parent §4.4, user decision U10): `Setting`/`ServerSetting` rows → (physical table, workercode, NAME). `SettingGroup` rows → (physical table, workercode, NAME, VALUE), except name-keyed groups (NAME starts with `Detail.`) → (physical table, workercode, NAME). A `SettingGroup` row with NAME exactly `Detail` is keyed by NAME+VALUE.
- `ServerSetting` values never leave the database: never in plan items, results, warnings or messages. The only items that may touch a `ServerSetting` row are renumbering `Update`s (Task 5): they carry `value`/`oldvalue`/`listvalue`/`oldlistvalue` = NULL, `ApplyPlan` matches them on identity + `oldsettingtype` + `oldordernr` and sets only `ordernr`; their inverses behave the same.
- Plan statuses (exact): `Created`, `Applied`, `Failed`, `Stale`, `RolledBack`. Session statuses: `Open`, `Closed`. Item actions: `CreateTable`, `Insert`, `Update`, `Delete`, `BackupTable`, `DropTable`.
- Error numbers → codes (exact; add to `SmErrorCodes`): 51006 `invalid_desired`, 51007 `serversetting`, 51008 `worker_layer`, 51009 `alias_target`, 51010 `table_missing`, 51011 `duplicate_row`, 51012 `session_not_found`, 51013 `session_closed`, 51014 `plan_not_found`, 51015 `plan_status`, 51016 `checksum_mismatch`. Stale and Failed are **not** errors: `ApplyPlan` returns them as status.
- Warning codes (exact): `OVERRIDE_PROC`, `LEGACY_ENV`, `ALIAS_TARGET`, `ROW_NOT_FOUND`, `ANCHOR_NOT_FOUND`, `RENUMBER`, `CREATE_TABLE`, `LEGACY_CONFLICT`, `STALE`.
- Backup table name `bck_{physicaltable}_{yyyyMMdd_HHmmss}` (UTC); if taken, append `_{seq}`; marker row `NAME='Backup.Description'`, `VALUE='Rollback of plan {id}: {description}'` (truncate to 400), `settingtype='BackupDescription'`, `workercode='SYSTEM'`, `ordernr=100000`.
- Created table template (exact): `NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL`.
- Plugin version `1.0.1` → `1.1.0`. Do not change UI code (`VTMF.Settings/`) or `settingssession`/`settingslog`.

## Review Focus

1. **A row changed in the UI between preview and apply** — apply refuses as `Stale`, changes nothing, lists the row with expected vs. current value. Test in Task 6 (`Apply_RowChangedAfterPreview_IsStaleAndUnchanged`).
2. **Undo after a later change to the same row** (another plan in the session, or the UI) — rollback plan is `Stale` on apply, nothing reverted, nothing partially reverted (U9). Test in Task 7 (`Rollback_AfterLaterChange_IsStale`).
3. **`SettingGroup` rows sharing a NAME** (`Column`=DOCNR, `Column`=DATE, `Detail`=A, `Detail`=B) — treated as distinct rows, not `duplicate_row`; but two `Setting` rows with the same NAME/workercode are refused. Test in Task 4 (`Groups_SharingName_AreDistinct`, `DuplicateSetting_IsRefused`).
4. **`ServerSetting` rows inside a replace scope** — never deleted, never shown, never cloned (masked source value must not overwrite target). Tests in Task 4 (`Replace_KeepsServerSettingRows`) and Task 10 (`Clone_SkipsServerSettingRows`).
5. **Merge insert into a full ordernr range** (no gap after the anchor) — table renumbered in steps of 10, renumbering visible as `Update` items with `RENUMBER` warning, final order correct. Test in Task 5 (`Insert_NoGap_RenumbersTable`).

---

### Task 1: Module v5 foundation — plan tables, error codes, test clients

**Files:**
- Create: `<sm>/100-script-plantables.txt`
- Modify: `<sm>/module.json` (version 5, sha256)
- Modify: `VTMF.Settings.Core/Sm/SmErrorCodes.cs`
- Modify: `VTMF.Settings.Tests/Sm/SmScriptSetTests.cs` (count 12 → 13)
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs` (clients `plan` → db `smplan`, `target` → db `smtarget`)
- Create: `VTMF.Settings.Tests/Sm/PlanSandbox.cs`
- Modify: `CLAUDE.md` (root): "no `BEGIN TRAN` in scripts" → "no top-level `BEGIN TRAN` in scripts (procedure bodies may use transactions)"
- Test: `VTMF.Settings.Tests/Sm/PlanTablesTests.cs`, `VTMF.Settings.Tests/Sm/SmErrorCodesTests.cs`

**Interfaces:**
- Produces tables (all in `sm`):
  - `session(id int IDENTITY PK, name nvarchar(200) NOT NULL, process nvarchar(100) NOT NULL, createdby nvarchar(128) NOT NULL, created datetime2(3) NOT NULL, lastactivity datetime2(3) NOT NULL, status nvarchar(10) NOT NULL)`
  - `plan(id int IDENTITY PK, sessionid int NULL FK→session, rollbackof int NULL, created datetime2(3), createdby nvarchar(128), process nvarchar(100), description nvarchar(400), source nvarchar(200), mode nvarchar(10), desired nvarchar(max) NULL CHECK (desired IS NULL OR ISJSON(desired)=1), checksum char(64), status nvarchar(12), appliedat datetime2(7) NULL, appliedby nvarchar(128) NULL, message nvarchar(max) NULL)`
  - `planitem(planid int FK, seq int, action nvarchar(12), physicaltable sysname, [table] nvarchar(100), layer nvarchar(110), workercode nvarchar(100) NULL, name nvarchar(200) NULL, value nvarchar(400) NULL, settingtype nvarchar(20) NULL, listvalue nvarchar(1600) NULL, ordernr int NULL, oldvalue nvarchar(400) NULL, oldsettingtype nvarchar(20) NULL, oldlistvalue nvarchar(1600) NULL, oldordernr int NULL, backuptable sysname NULL, PK(planid, seq))`. `name` is the physical NAME (process prefix included for `ServerProcessSettings`). Insert: `old*` NULL. Delete: new columns NULL, row data in `old*` (with `value` NULL and `oldvalue` set).
  - `planwarning(planid int FK, seq int IDENTITY, severity nvarchar(10), code nvarchar(30), message nvarchar(1000))`
- Produces `SmErrorCodes` constants: `InvalidDesired = "invalid_desired"`, `ServerSetting = "serversetting"`, `WorkerLayer = "worker_layer"`, `AliasTarget = "alias_target"`, `TableMissing = "table_missing"`, `DuplicateRow = "duplicate_row"`, `SessionNotFound = "session_not_found"`, `SessionClosed = "session_closed"`, `PlanNotFound = "plan_not_found"`, `PlanStatus = "plan_status"`, `ChecksumMismatch = "checksum_mismatch"`, mapped from 51006–51016 in `FromSqlErrorNumber`.
- Produces fixture: `SqlServerFixture.PlanClientName = "plan"`, `TargetClientName = "target"` (both in `AllClientNames`, base scripts run, module installed, no seed).
- Produces `PlanSandbox` (test helper, `internal static`):
  - `Task<string> CreateProcessAsync(SqlServerFixture f, string client, string? process = null, string? settingProcedures = null)` — inserts a `processtypes` row (name `pl` + 8 lowercase hex chars unless given; `ENVIRONMENT='wterminal'`) and table `{p}DocumentListLayout` (template columns) seeded with: (`Column`,`DOCNR`,SettingGroup,10), (`Column.DOCNR.Caption`,`Dok nr`,Setting,20), (`Column`,`DATE`,SettingGroup,30), (`Column.DATE.Caption`,`Kuupäev`,Setting,40), (`Grid.PageSize`,`20`,Setting,50), (`Grid.Secret`,`abc`,ServerSetting,60), all workercode `ALL`; plus `ServerProcessSettings` row (`{p}.Function.Barcode.Parser`,`Parserbuilder`,Setting,`ALL`,0). Returns the process name.
  - `Task<IReadOnlyList<SmSettingRow>> RowsAsync(SqlServerFixture f, string client, string physicalTable, string workercode = "ALL")` — direct `SELECT` (unmasked, ordered by ordernr, NAME), for assertions.
  - `Task ExecAsync(SqlServerFixture f, string client, string sql)`.

- [ ] **Step 1: Write failing tests.** `SmErrorCodesTests` (no Integration trait): `[Theory]` over the 11 pairs, e.g. `[InlineData(51006, "invalid_desired")]`…`[InlineData(51016, "checksum_mismatch")]` → `Assert.Equal(code, SmErrorCodes.FromSqlErrorNumber(number))` (make `FromSqlErrorNumber` visible via existing `InternalsVisibleTo` or make it `public`). `PlanTablesTests` (Integration): `Install_CreatesPlanTables` → for `plan` client, `OBJECT_ID('sm.session','U')`, `sm.plan`, `sm.planitem`, `sm.planwarning` all non-null; `Install_IsIdempotent` → `fixture.Installer.InstallAsync("plan")` twice more succeeds and version is 5.
- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SmErrorCodesTests|FullyQualifiedName~PlanTablesTests"` → FAIL.
- [ ] **Step 3: Implement** script, error codes, fixture clients, `PlanSandbox`, CLAUDE.md wording; set `module.json` version 5.
- [ ] **Step 4: Run** the same filter plus `FullyQualifiedName~SmScriptSetTests` → PASS after pasting the new sha256.
- [ ] **Step 5: Commit** `feat(sm): plan engine tables, error codes (module v5)`.

---

### Task 2: Sessions

**Files:**
- Create: `<sm>/110-procedure-StartSession.txt`, `<sm>/111-procedure-GetSessions.txt`, `<sm>/112-procedure-CloseSession.txt`
- Modify: `VTMF.Settings.Core/Sm/SmModels.cs`, `VTMF.Settings.Core/Sm/SmClient.cs`, `<sm>/module.json`, `SmScriptSetTests` count
- Test: `VTMF.Settings.Tests/Sm/SessionTests.cs`

**Interfaces:**
- SQL: `sm.StartSession @name nvarchar(200), @process nvarchar(100)` → one row (session columns + `plancount`, `appliedcount`); `@name` blank → 51006; unknown process → 51003. `sm.GetSessions @process nvarchar(100) = NULL, @top int = 20` → newest first. `sm.CloseSession @sessionid int` → the row; unknown → 51012; already closed → returns row unchanged.
- `public sealed class SmSession { int Id; string Name; string Process; string CreatedBy; DateTime Created; DateTime LastActivity; string Status; int PlanCount; int AppliedCount; }` (`get; set;` Dapper style).
- `SmClient`: `Task<SmSession> StartSessionAsync(string client, string name, string process, CancellationToken cancellationToken = default)`, `Task<IReadOnlyList<SmSession>> GetSessionsAsync(string client, string? process = null, int top = 20, CancellationToken cancellationToken = default)`, `Task<SmSession> CloseSessionAsync(string client, int sessionId, CancellationToken cancellationToken = default)`. All gated (use `QueryAsync`).

- [ ] **Step 1: Failing tests** (Integration, `[Collection(SqlCollection.Name)]`, client `plan`, process from `PlanSandbox.CreateProcessAsync`):
  - `StartSession_ReturnsOpenSession`: `Status == "Open"`, `Name == "LOT column"`, `CreatedBy` not empty, `PlanCount == 0`.
  - `GetSessions_NewestFirst_FilteredByProcess`: two sessions on p1, one on p2 → `GetSessionsAsync(client, p1)` returns 2, first has the higher id.
  - `CloseSession_SetsClosed`; `CloseSession_Unknown_ThrowsSessionNotFound` (`SmException.Code == SmErrorCodes.SessionNotFound`).
  - `StartSession_BlankName_ThrowsInvalidDesired`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~SessionTests"` → FAIL.
- [ ] **Step 3: Implement** scripts, model, client methods.
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~SessionTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): sessions`.

---

### Task 3: `sm.CreatePlan` core — parse, resolve, diff, store, checksum, read back

**Files:**
- Create: `<sm>/120-procedure-PlanResolve.txt`, `<sm>/121-procedure-PlanLoadCurrent.txt`, `<sm>/122-procedure-PlanDiff.txt`, `<sm>/124-procedure-PlanStore.txt`, `<sm>/125-function-PlanChecksum.txt`, `<sm>/129-procedure-CreatePlan.txt`, `<sm>/130-procedure-GetPlan.txt`, `<sm>/131-procedure-GetPlans.txt`
- Modify: `SmModels.cs`, `SmClient.cs`, `module.json`, `SmScriptSetTests` count
- Test: `VTMF.Settings.Tests/Sm/CreatePlanTests.cs`

**Interfaces:**
- SQL entry point: `sm.CreatePlan @desired nvarchar(max), @description nvarchar(400), @source nvarchar(200) = N'manual', @options nvarchar(max) = NULL, @sessionid int = NULL, @dryrun bit = 0`. Returns three result sets: (1) header `planid int NULL, checksum char(64) NULL, status nvarchar(12)` where status is `Created`, `NoChanges` (empty diff, also in dry run) or `DryRun` (non-empty diff, `@dryrun=1`); no plan stored for the latter two, `planid`/`checksum` NULL; (2) items (planitem columns minus `planid`); (3) warnings (`severity, code, message`).
- Internal procs share caller-created temp tables (`#desired`, `#scope`, `#current`, `#items`, `#warnings`), created in `sm.CreatePlan` and reused by later tasks' procedures (rollback, migration) — the column lists are defined once in `129-procedure-CreatePlan.txt` and documented in a header comment there.
- Desired JSON (`OPENJSON`): `{ process, mode: "merge"|"replace", scope: [{table, layer}], rows: [{table, layer, name, value, settingtype, listvalue, ordernr, op: "upsert"|"remove", after}] }`. Defaults: `layer` `base`, `settingtype` `Setting`, `op` `upsert`. `@options`: `{ createTable: bool, targetAlias: bool }`.
- Validation → 51006: invalid JSON, unknown mode, missing process/name, `value` NULL on upsert, replace row outside `scope`, replace row without `ordernr`, unsafe table name, `targetAlias` and `createTable` both true. Unknown process → 51003. Bad layer → 51002, except a layer starting with `worker:` → 51008 with message "Worker-specific rows are not managed. Use env:{environment}, lang:{code}, or a process-type override." `ServerProcessSettings` with non-base layer → 51005. Session given and `Closed` → 51013; unknown → 51012.
- Resolution: physical table = `{process}{table}{suffix}` (`suffix` from `sm.ResolveLayer`), `ServerProcessSettings` stays as is with NAME prefixed `{process}.`; workercode from `sm.ResolveLayer`.
- Diff (this task: Setting rows and `ordernr` given or appended at end = max(ordernr)+10, or 0 for `ServerProcessSettings`): merge upsert → `Insert` if identity absent, `Update` if any of VALUE, settingtype, LISTVALUE, or ordernr (when given) differs, nothing if equal; merge `remove` → `Delete` if present, else warning `ROW_NOT_FOUND`; replace → upserts as merge plus `Delete` for every current row in each scope (table, layer) whose identity is not desired. Item seq order: `CreateTable` items, then by physical table, then `Delete`, `Update`, `Insert` by resulting ordernr.
- Store: `sm.plan` row (`status='Created'`, `desired=@desired`, `mode`, `process`, `createdby=ORIGINAL_LOGIN()`), items, warnings; `checksum = sm.PlanChecksum(@planid)`; touches `sm.session.lastactivity`.
- `sm.PlanChecksum(@planid int) RETURNS char(64)`:
  ```sql
  LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256',
      (SELECT seq, action, physicaltable, workercode, name, value, settingtype, listvalue, ordernr,
              oldvalue, oldsettingtype, oldlistvalue, oldordernr
       FROM sm.planitem WHERE planid = @planid ORDER BY seq
       FOR JSON PATH, INCLUDE_NULL_VALUES)), 2))
  ```
- `sm.GetPlan @planid` → (1) plan row (all `sm.plan` columns except `desired`), (2) items, (3) warnings; unknown → 51014. `sm.GetPlans @process = NULL, @sessionid = NULL, @top int = 20` → plan rows, newest first.
- C# models:
  - `SmDesiredState { string Process; string Mode = "merge"; IReadOnlyList<SmScopeEntry> Scope = []; IReadOnlyList<SmDesiredRow> Rows = []; }`, `SmScopeEntry { string Table; string Layer = "base"; }`, `SmDesiredRow { string Table; string Layer = "base"; string Name; string? Value; string SettingType = "Setting"; string? ListValue; int? OrderNr; string Op = "upsert"; string? After; }`, `SmPlanOptions { bool CreateTable; bool TargetAlias; }` — `init` properties, JSON names lowercase (`[JsonPropertyName("settingtype")]` etc., serialized with `System.Text.Json`, nulls omitted).
  - `SmPlanItem { int Seq; string Action; string PhysicalTable; string Table; string Layer; string? WorkerCode; string? Name; string? Value; string? SettingType; string? ListValue; int? OrderNr; string? OldValue; string? OldSettingType; string? OldListValue; int? OldOrderNr; string? BackupTable; }`
  - `SmPlanWarning { string Severity; string Code; string Message; }`
  - `SmPlanResult { int? PlanId; string? Checksum; string Status; IReadOnlyList<SmPlanItem> Items; IReadOnlyList<SmPlanWarning> Warnings; }` (`required init`)
  - `SmPlan { int Id; int? SessionId; int? RollbackOf; DateTime Created; string CreatedBy; string Process; string Description; string Source; string Mode; string Checksum; string Status; DateTime? AppliedAt; string? AppliedBy; string? Message; }`
  - `SmPlanDetails { SmPlan Plan; IReadOnlyList<SmPlanItem> Items; IReadOnlyList<SmPlanWarning> Warnings; }`
- `SmClient`: `Task<SmPlanResult> CreatePlanAsync(string client, SmDesiredState desired, string description, int? sessionId, SmPlanOptions? options = null, string source = "manual", bool dryRun = false, CancellationToken cancellationToken = default)`; `Task<SmPlanDetails> GetPlanAsync(string client, int planId, CancellationToken cancellationToken = default)`; `Task<IReadOnlyList<SmPlan>> GetPlansAsync(string client, string? process = null, int? sessionId = null, int top = 20, CancellationToken cancellationToken = default)`. Multi-result-set pattern as `GetEffectiveSettingsAsync`.

- [ ] **Step 1: Failing tests** (`CreatePlanTests`, client `plan`, fresh process + session per test):
  - `Merge_UpdateValue_CreatesUpdateItem`: row `Grid.PageSize`=`50` → one item `Action=="Update"`, `Value=="50"`, `OldValue=="20"`, `PhysicalTable==p+"DocumentListLayout"`, `WorkerCode=="ALL"`; status `Created`; `Checksum` 64 lowercase hex; table unchanged (`RowsAsync`).
  - `Merge_NewRowWithOrderNr_CreatesInsert`: `Grid.ShowFilterRow`=`true`, ordernr 55 → `Insert`, `OrderNr==55`, `OldValue==null`.
  - `Merge_Remove_CreatesDelete` and `Merge_RemoveMissing_WarnsRowNotFound`.
  - `Merge_SameValue_ReturnsNoChanges`: status `NoChanges`, `PlanId==null`, `GetPlansAsync(client, p)` empty.
  - `Replace_DeletesRowsNotListed`: scope base `DocumentListLayout`, rows = current rows except `Grid.PageSize` (with ordernr) → exactly one `Delete` item for `Grid.PageSize`.
  - `Replace_RowWithoutOrderNr_ThrowsInvalidDesired`; `InvalidJsonMode_ThrowsInvalidDesired`; `WorkerLayer_ThrowsWorkerLayer` (layer `worker:bob`); `ClosedSession_ThrowsSessionClosed`.
  - `ServerProcessSettings_PrefixesName`: table `ServerProcessSettings`, name `Function.Barcode.Parser`, value `X` → `Update`, `Name == p + ".Function.Barcode.Parser"`.
  - `DryRun_ReturnsItemsWithoutStoring`: status `DryRun`, items non-empty, `GetPlansAsync` empty.
  - `GetPlan_ReturnsStoredItemsAndChecksumMatchesHeader`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~CreatePlanTests"` → FAIL.
- [ ] **Step 3: Implement** the scripts and C# above.
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~CreatePlanTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): CreatePlan core diff, store, checksum`.

---

### Task 4: Guardrails, group identity, duplicates, table creation

**Files:**
- Create: `<sm>/123-procedure-PlanGuardrails.txt` (called from `sm.CreatePlan` after resolve and load)
- Modify: `120`, `121`, `122`, `129` scripts as needed; `module.json`; script count
- Test: `VTMF.Settings.Tests/Sm/PlanGuardrailTests.cs`

**Interfaces:** (codes/numbers per Global Constraints; errors are collected in `#warnings` with severity `Error`, then the first error's number is thrown with all error messages joined by newline; nothing stored)
- `SERVERSETTING` (51007): desired row with settingtype `ServerSetting`, or desired identity matching an existing `ServerSetting` row. In replace scope, existing `ServerSetting` rows are never deleted (silently kept). Renumbering `Update`s (Task 5) are exempt.
- `ALIAS_TARGET` (51009): process has `SETTINGPROCEDURES` ≠ own NAME and no own physical table for the target (view, layer): error unless `targetAlias=true` → physical table = alias process table, warning `ALIAS_TARGET` naming the variants (`processtypes` rows with the same `SETTINGPROCEDURES`), or `createTable=true` → own table created (warning `CREATE_TABLE`).
- `TABLE_MISSING` (51010): physical table missing and no alias: error unless `createTable=true` → `CreateTable` item first in seq, warning `CREATE_TABLE`; rows go in as `Insert`.
- `DUPLICATE_ROW` (51011): a touched identity (desired row, or replace-scope current row) has more than one physical match; message names table, workercode, NAME (and VALUE for groups).
- `OVERRIDE_PROC` (warning): active `Get{process}{view}Info` exists for a touched base table (same rule as `sm.GetTables`).
- `LEGACY_ENV` (warning): a touched base table has rows with `workercode <> 'ALL'`.
- Group identity per Global Constraints; a non-name-keyed `SettingGroup` with a different VALUE is a different row (`Insert`), so its only possible `Update` is ordernr/LISTVALUE.

- [ ] **Step 1: Failing tests:**
  - `ServerSettingRow_ThrowsServerSetting` (desired `Grid.Secret` upsert), and assert the exception message does not contain `abc`.
  - `Replace_KeepsServerSettingRows`: replace scope base with all rows except `Grid.Secret` → no item touches `Grid.Secret`; no error.
  - `Groups_SharingName_AreDistinct`: merge upsert (`Column`,`LOT`,SettingGroup, ordernr 35) → one `Insert`; no `Delete`/`Update` of `Column`=`DOCNR`/`DATE`. Insert two `Detail` group rows (`Detail`=`A`, `Detail`=`B`) via `ExecAsync`, then upsert (`Detail`,`A`, ordernr 99) → one `Update` (ordernr) for `Detail`=`A` only.
  - `NameKeyedGroup_ValueChangeIsUpdate`: existing (`Detail.Lines`,`X`,SettingGroup) → upsert value `Y` → `Update` with `OldValue=="X"`.
  - `DuplicateSetting_IsRefused`: insert a second `Grid.PageSize` row (workercode ALL) → upsert `Grid.PageSize` throws `duplicate_row`; upsert of untouched `Column.DATE.Caption` still works.
  - `MissingTable_ThrowsTableMissing`; `MissingTable_WithCreateTable_PlansCreateTable`: table `DocumentNewLayout` → items[0] `CreateTable` with `PhysicalTable == p+"DocumentNewLayout"`, then `Insert`; warning `CREATE_TABLE`.
  - `AliasProcess_WithoutOwnTable_ThrowsAliasTarget`; `AliasProcess_TargetAlias_WritesAliasTableAndWarns` (variant created with `settingProcedures: p`).
  - `OverrideProc_Warns` (create `dbo.Get{p}DocumentListLayoutInfo`); `LegacyEnvRow_Warns` (insert a `wterminal2` row in base table).
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~PlanGuardrailTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~PlanGuardrailTests|FullyQualifiedName~CreatePlanTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): plan guardrails, group identity, duplicate refusal`.

---

### Task 5: Ordering and renumbering (merge inserts without ordernr)

**Files:**
- Modify: `<sm>/122-procedure-PlanDiff.txt` (or create `<sm>/126-procedure-PlanOrder.txt` called after diff); `module.json`; count if a file is added
- Test: `VTMF.Settings.Tests/Sm/PlanOrderingTests.cs`

**Interfaces:** Applies per (physical table, workercode), not to `ServerProcessSettings` (new rows there get ordernr 0). Desired new rows are processed in their desired-list order.
- Sort key of a row: `ordernr`, then NAME, then VALUE. Path of a row: NAME, or `NAME + '.' + VALUE` for non-name-keyed `SettingGroup` rows.
- Anchor: `after` = `NAME` or `NAME=VALUE` → that row (an existing or an earlier new row). Not found → warning `ANCHOR_NOT_FOUND`, fall through. No `after`: the last row (by sort key) whose path shares the most leading dot-segments (≥ 1) with the new row's path. None → append at end.
- Placement: immediately after the anchor (and after any new rows already placed after that anchor). Let A = anchor ordernr, N = next row's ordernr. If no next row → A + 10. If N − A ≥ 2 → A + (N − A) / 2. Else **renumber** the whole (physical table, workercode): final order with new rows placed, ordernr = 10, 20, 30…; existing rows whose ordernr changes get `Update` items; one warning `RENUMBER` naming the table.
- Appending at end: max(ordernr) + 10 (10 for an empty table).

- [ ] **Step 1: Failing tests** (sandbox ordernrs 10..60):
  - `Insert_AfterAnchor_UsesMidpoint`: (`Column.DOCNR.Width`, `100`), `after = "Column.DOCNR.Caption"` → ordernr 25.
  - `Insert_AfterGroupAnchor`: `after = "Column=DATE"` → ordernr 35.
  - `Insert_NoAfter_UsesLongestPrefix`: `Column.DATE.Width` → after `Column.DATE.Caption` (40) → 45.
  - `Insert_NoMatch_AppendsAtEnd`: `Zzz.New` → 70.
  - `Insert_NoGap_RenumbersTable`: set `Column.DOCNR.Caption` ordernr to 11 via `ExecAsync`; insert `Column.DOCNR.Width` with `after = "Column=DOCNR"` (10, next 11) → `Insert` ordernr 20; `Update`s exactly: `Column.DOCNR.Caption` 11→30, `Column`=`DATE` 30→40, `Column.DATE.Caption` 40→50, `Grid.PageSize` 50→60, `Grid.Secret` 60→70 with `Value == null && OldValue == null`; no item for `Column`=`DOCNR`; one `RENUMBER` warning. (Applied in Task 6 `Apply_Renumber_FinalOrderCorrect`.)
  - `Insert_AnchorNotFound_WarnsAndFallsBack`.
  - `TwoNewRows_SameAnchor_KeepDesiredOrder`: rows X then Y both `after = "Grid.PageSize"` → X.ordernr < Y.ordernr < 60.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~PlanOrderingTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~PlanOrderingTests|FullyQualifiedName~CreatePlanTests|FullyQualifiedName~PlanGuardrailTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): plan ordering and renumbering`.

---

### Task 6: `sm.ApplyPlan`

**Files:**
- Create: `<sm>/140-procedure-ApplyPlan.txt`, `<sm>/141-procedure-PlanCheckItems.txt` (read-only conflict check used for the Stale report)
- Modify: `SmModels.cs`, `SmClient.cs`, `module.json`, count
- Test: `VTMF.Settings.Tests/Sm/ApplyPlanTests.cs`

**Interfaces:**
- `sm.ApplyPlan @planid int, @checksum char(64)` → (1) `planid, status, message`, (2) conflicts `seq, action, physicaltable, workercode, name, expectedvalue, currentvalue, reason` (`reason`: `changed` | `missing` | `exists` | `duplicate` | `table_exists` | `table_missing` | `table_not_empty`). Unknown plan → 51014; status ≠ `Created` → 51015; `@checksum` ≠ stored or stored ≠ recomputed `sm.PlanChecksum` → 51016 (nothing changed, status unchanged).
- Algorithm: `SET XACT_ABORT ON`; `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE`; `BEGIN TRAN`; lock plan row (`UPDLOCK, HOLDLOCK`); for each item in seq: check precondition, then apply via `sp_executesql` on `QUOTENAME(physicaltable)`:
  - `Insert`: identity absent → INSERT. `Update`/`Delete`: exactly one row with identity and old image (`oldvalue`, `oldsettingtype`, `oldlistvalue`, `oldordernr`, NULL-safe) → UPDATE/DELETE. `CreateTable`: table absent → CREATE (template). `BackupTable`: table present → `SELECT * INTO` backup name (Global Constraints) + marker row; store name in `planitem.backuptable`. `DropTable`: table present and empty → DROP.
  - First failed precondition: `ROLLBACK`; run `sm.PlanCheckItems @planid` (evaluates every row item against current state without applying; reports all conflicts); set status `Stale`, add `STALE` warning per conflict (no `ServerSetting` values; values of the plan's own rows only); return status `Stale` + conflicts.
  - All applied: status `Applied`, `appliedat = SYSUTCDATETIME()`, `appliedby = ORIGINAL_LOGIN()`; if `rollbackof` is set → that plan `RolledBack`; if `source` starts with `rollback-session:` → all `Applied` plans of the session with `appliedat` < this plan's `created` → `RolledBack`; `COMMIT`; touch session `lastactivity`.
  - `CATCH` (any other error): `ROLLBACK` if open, status `Failed`, `message = ERROR_MESSAGE()`; return status `Failed` (no rethrow).
- C#: `SmApplyResult { int PlanId; string Status; string? Message; IReadOnlyList<SmPlanConflict> Conflicts; }`, `SmPlanConflict { int Seq; string Action; string PhysicalTable; string? WorkerCode; string? Name; string? ExpectedValue; string? CurrentValue; string Reason; }`. `SmClient.ApplyPlanAsync(string client, int planId, string checksum, CancellationToken cancellationToken = default) → Task<SmApplyResult>`.

- [ ] **Step 1: Failing tests:**
  - `Apply_UpdatesRowsAndMarksApplied`: plan `Grid.PageSize`=50 → result `Applied`; `RowsAsync` shows 50; `GetPlanAsync` status `Applied`, `AppliedBy` not empty.
  - `Apply_WrongChecksum_ThrowsChecksumMismatch_AndKeepsCreated`.
  - `Apply_Twice_ThrowsPlanStatus`.
  - `Apply_RowChangedAfterPreview_IsStaleAndUnchanged`: create plan updating `Grid.PageSize` and `Column.DATE.Caption`; then `UPDATE … SET VALUE='30' WHERE NAME='Grid.PageSize'` → result `Stale`; one conflict `Reason=="changed"`, `ExpectedValue=="20"`, `CurrentValue=="30"`; `Column.DATE.Caption` unchanged.
  - `Apply_InsertedMeanwhile_IsStaleExists`.
  - `Apply_CreateTable_CreatesAndInserts`: plan with `createTable` for `DocumentNewLayout` → table exists with the inserted row; column set equals the template.
  - `Apply_FailureMidPlan_RollsBackAndMarksFailed`: create plan with two inserts into a table, then make the second impossible without changing preconditions (e.g. `ALTER TABLE … ADD CONSTRAINT CK CHECK (VALUE <> 'boom')` with second row value `boom`) → status `Failed`, `Message` not empty, first row not present.
  - `Apply_Renumber_FinalOrderCorrect` (Task 5 `Insert_NoGap` setup): after apply, rows ordered by ordernr have NAMEs `Column`(DOCNR), `Column.DOCNR.Width`, `Column.DOCNR.Caption`, `Column`(DATE), `Column.DATE.Caption`, `Grid.PageSize`, `Grid.Secret` with ordernr 10..70; `Grid.Secret` still has value `abc`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~ApplyPlanTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~ApplyPlanTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): ApplyPlan with stale detection`.

---

### Task 7: Rollback plans (incl. `CreateTable` → backup + drop, redo)

**Files:**
- Create: `<sm>/150-procedure-PlanInvert.txt` (fills `#items` with the inverse of a caller-created `#source` item set), `<sm>/151-procedure-CreateRollbackPlan.txt`
- Modify: `SmClient.cs`, `module.json`, count
- Test: `VTMF.Settings.Tests/Sm/RollbackPlanTests.cs`

**Interfaces:**
- `sm.CreateRollbackPlan @planid int` → same three result sets as `sm.CreatePlan`. Unknown → 51014; status ≠ `Applied` → 51015. Stored with `rollbackof = @planid`, same `sessionid`, `process`, `mode='merge'`, `desired=NULL`, `source='rollback:{planid}'`, `description='Rollback of plan {id}: {original description}'` (truncate 400). Allowed for `Closed` sessions.
- Inversion (`sm.PlanInvert`): first, one `BackupTable` per `CreateTable` in the source; then source items in **reverse** seq: `Insert`→`Delete` (old image = inserted values), `Delete`→`Insert`, `Update`→`Update` with images swapped, `CreateTable`→`DropTable`, `DropTable`→`CreateTable`, `BackupTable`→nothing. Renumber seq 1..n.
- `SmClient.CreateRollbackPlanAsync(string client, int planId, CancellationToken cancellationToken = default) → Task<SmPlanResult>`.

- [ ] **Step 1: Failing tests:**
  - `Rollback_RestoresValues_AndMarksOriginalRolledBack`: apply `Grid.PageSize`=50, rollback plan (one `Update` 50→20), apply → value 20, original `RolledBack`, rollback plan `RollbackOf == original`.
  - `Rollback_NotApplied_ThrowsPlanStatus`.
  - `Rollback_AfterLaterChange_IsStale`: apply plan A (`Grid.PageSize`=50, `Column.DATE.Caption`=`Date`); then UI-style `UPDATE` `Grid.PageSize`=60; rollback A and apply → `Stale`; `Column.DATE.Caption` still `Date` (no partial undo); A still `Applied`.
  - `Rollback_CreateTable_BacksUpAndDrops`: apply createTable plan for `DocumentNewLayout` (2 rows); rollback items are `BackupTable`, `Delete`, `Delete`, `DropTable`; apply → table gone; `GetPlanAsync(rollback).Items[0].BackupTable` starts with `"bck_" + p + "DocumentNewLayout_"`; that backup table holds the 2 rows + marker row `Backup.Description` with `settingtype='BackupDescription'`, `workercode='SYSTEM'`, `ordernr=100000`, value starting `Rollback of plan `.
  - `Rollback_CreateTable_WithForeignRow_IsStale`: after the create plan, insert an extra row by SQL → rollback apply `Stale`, conflict reason `table_not_empty`, table still exists.
  - `RollbackOfRollback_RecreatesTable`: continue the backup test: rollback of the rollback plan → `CreateTable`, 2 `Insert`s; apply → table back with both rows.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~RollbackPlanTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~RollbackPlanTests|FullyQualifiedName~ApplyPlanTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): rollback plans with backup-before-drop`.

---

### Task 8: Session net change and session rollback

**Files:**
- Create: `<sm>/160-procedure-SessionNetChanges.txt` (fills caller-created `#source` with the net change), `<sm>/161-procedure-GetSessionChanges.txt`, `<sm>/162-procedure-CreateSessionRollbackPlan.txt`
- Modify: `SmClient.cs`, `module.json`, count
- Test: `VTMF.Settings.Tests/Sm/SessionChangesTests.cs`

**Interfaces:**
- Net change over items of plans in the session with status `Applied` or `RolledBack` (i.e. that were applied), ordered by `appliedat`, then `seq`. Row key = identity (Global Constraints). Per key: first item's old image (NULL for `Insert`), last item's new image (NULL for `Delete`); omit if equal (NULL-safe on VALUE, settingtype, LISTVALUE, ordernr). Result action: old NULL → `Insert`; new NULL → `Delete`; else `Update`. Tables: net `CreateTable` if created in the session and existing now; net `DropTable` if existing at session start and dropped. Output in the `SmPlanItem` shape (seq = output order: `CreateTable`, rows, `DropTable`).
- `sm.GetSessionChanges @sessionid` → items; unknown → 51012.
- `sm.CreateSessionRollbackPlan @sessionid` → `sm.PlanInvert` over the net change; stored with `source='rollback-session:{id}'`, `rollbackof=NULL`, same session, description `Rollback of session {id}: {name}`. Empty net change → status `NoChanges`. Marking of covered plans happens in `ApplyPlan` (Task 6).
- `SmClient.GetSessionChangesAsync(string client, int sessionId, CancellationToken cancellationToken = default) → Task<IReadOnlyList<SmPlanItem>>`; `CreateSessionRollbackPlanAsync(string client, int sessionId, CancellationToken cancellationToken = default) → Task<SmPlanResult>`.

- [ ] **Step 1: Failing tests:**
  - `NetChange_RevertedValueDropsOut`: apply PageSize 20→50, then 50→20, then `Column.DATE.Caption` → `Date` → net change = one `Update` for `Column.DATE.Caption`.
  - `NetChange_ValueChangedThreeTimes_ShowsFirstAndLast`: 20→30→40→50 → one `Update` `OldValue=="20"`, `Value=="50"`.
  - `NetChange_InsertThenUpdate_IsInsertWithFinalValue`.
  - `NetChange_IncludesCreatedTable`.
  - `SessionRollback_RestoresStartState`: three plans (update, insert, createTable+insert) → session rollback applied → `RowsAsync` equals the snapshot taken at session start; created table gone; all three plans `RolledBack`.
  - `SessionRollback_NothingChanged_ReturnsNoChanges`.
  - `SessionRollback_AfterUiChange_IsStale` (UI-style update on a touched row → `Stale`, nothing reverted).
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~SessionChangesTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~SessionChangesTests|FullyQualifiedName~RollbackPlanTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): session net change and session rollback`.

---

### Task 9: Legacy env migration plan

**Files:**
- Create: `<sm>/170-procedure-CreateLegacyEnvMigrationPlan.txt`
- Modify: `SmClient.cs`, `module.json`, count
- Test: `VTMF.Settings.Tests/Sm/LegacyEnvMigrationTests.cs`

**Interfaces:**
- `sm.CreateLegacyEnvMigrationPlan @process nvarchar(100), @sessionid int` → CreatePlan result sets; `source='migration'`, `mode='merge'`, `desired=NULL`. For each base-table row with `workercode = E <> 'ALL'` (skip `ServerSetting` rows and `E` failing `sm.IsSafeName`, warning `LEGACY_CONFLICT` for unsafe `E`): overlay `{table}{E}` (workercode `E`); missing overlay → `CreateTable` item (no option needed; warning `CREATE_TABLE`). Overlay has the identity with equal VALUE/settingtype/LISTVALUE → only `Delete` of the legacy row. Overlay has the identity with a different value → no items for that row, warning `LEGACY_CONFLICT` naming table, E, NAME. Otherwise `Insert` into overlay (same ordernr) + `Delete` from base. Guardrails `DUPLICATE_ROW` apply. No legacy rows → `NoChanges`.
- `SmClient.CreateLegacyEnvMigrationPlanAsync(string client, string process, int sessionId, CancellationToken cancellationToken = default) → Task<SmPlanResult>`.

- [ ] **Step 1: Failing tests:**
  - `Migration_MovesRowToNewOverlay`: legacy row (`Grid.PageSize`,`30`,`wterminal2`) → items `CreateTable` (`{p}DocumentListLayoutwterminal2`), `Insert` (workercode `wterminal2`), `Delete` (base); after apply, `sm.GetEffectiveSettings` for env `wterminal2` still returns `30` for `Grid.PageSize`.
  - `Migration_SameValueInOverlay_OnlyDeletesLegacy`.
  - `Migration_ConflictingOverlay_WarnsAndKeepsLegacy`.
  - `Migration_NoLegacyRows_ReturnsNoChanges`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~LegacyEnvMigrationTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `-Filter "FullyQualifiedName~LegacyEnvMigrationTests|FullyQualifiedName~SmScriptSetTests"` → PASS.
- [ ] **Step 5: Commit** `feat(sm): legacy env migration plan`.

---

### Task 10: `SmOrchestrator` — compare and clone

**Files:**
- Create: `VTMF.Settings.Core/Sm/SmOrchestrator.cs`
- Modify: `VTMF.Settings.Core/Sm/SmServiceCollectionExtensions.cs` (`AddSingleton<SmOrchestrator>()`)
- Test: `VTMF.Settings.Tests/Sm/SmOrchestratorTests.cs` (clients `plan` = source, `target`)

**Interfaces:**
- `public sealed class SmOrchestrator(SmClient sm)`:
  - `Task<SmPlanResult> CompareClientsAsync(string source, string target, string process, IReadOnlyList<SmScopeEntry>? scope = null, CancellationToken cancellationToken = default)` → `CreatePlanAsync(target, desired, "Compare with {source}", sessionId: null, options: new() { CreateTable = true }, source: $"clone:{source}", dryRun: true)`. Missing tables then show as `CreateTable` items.
  - `Task<SmPlanResult> ClonePlanAsync(string source, string target, string process, int sessionId, IReadOnlyList<SmScopeEntry>? scope = null, SmPlanOptions? options = null, CancellationToken cancellationToken = default)` → same desired, stored, `description = $"Clone {process} from {source}"`.
  - Desired builder (private): default scope = every `GetTablesAsync(source, process)` entry owned by `process` (not alias-owned) → (view, layer), plus (`ServerProcessSettings`, `base`); rows = `GetSettingsAsync(source, …)` for each scope entry, **excluding `ServerSetting` rows**, with `ordernr` from source; `mode = "replace"`. An explicit `scope` restricts to those entries.
- Target must contain the process (`processtypes`) — else `process_not_found` from `CreatePlan`.

- [ ] **Step 1: Failing tests** (`PlanSandbox.CreateProcessAsync` with the **same** process name in both clients; then change source):
  - `Compare_IdenticalClients_NoChangesStatus` → `Status == "NoChanges"`, `Items` empty (an empty diff is `NoChanges` even in a dry run; `DryRun` only when there are items).
  - `Compare_ShowsDifferences_WithoutStoring`: source `Grid.PageSize`=99 → one `Update` item; `GetPlansAsync(target)` empty.
  - `Clone_CreatesPlanOnTarget_ApplyMakesEqual`: source has an extra row + changed value + table `DocumentNewLayout` → `ClonePlanAsync(..., options: new() { CreateTable = true })`, apply on target → `GetSettingsAsync` for each scope entry equal between clients (excluding `ServerSetting` rows).
  - `Clone_SkipsServerSettingRows`: target `Grid.Secret` value stays `abc` after clone apply when source has it `zzz`; no item names `Grid.Secret`.
  - `Clone_MissingTable_WithoutOption_ThrowsTableMissing`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~SmOrchestratorTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** same filter → PASS.
- [ ] **Step 5: Commit** `feat(core): SmOrchestrator compare and clone`.

---

### Task 11: MCP tools

**Files:**
- Create: `VTMF.Settings.Mcp/Tools/SettingsPlanTools.cs`
- Modify: `VTMF.Settings.Mcp/McpServerSetup.cs` (`.WithTools<SettingsPlanTools>()`; `Instructions` gains one sentence: "To change settings, start a session, create a plan, show its items to the user, and call apply_plan only after the user confirms; never change settings any other way.")
- Modify: `VTMF.Settings.Mcp/Program.cs` only if `SmOrchestrator` is not registered through `AddSmClient`
- Test: `VTMF.Settings.Tests/Mcp/SettingsPlanToolsTests.cs`, update `McpServerSetupTests` if it asserts tool names/instructions

**Interfaces:** `public sealed class SettingsPlanTools(SmClient sm, SmOrchestrator orchestrator)`, class attributes as `SettingsReadTools`; every method `=> ToolResult.RunAsync(() => …)`. Names, annotations, parameters:

| Tool | Annotations | Parameters → returns |
|---|---|---|
| `start_session` | `ReadOnly=false, Destructive=false` | `client, name, process` → `SmSession` |
| `list_sessions` | `ReadOnly=true` | `client, process?=null, top=20` → `IReadOnlyList<SmSession>` |
| `session_changes` | `ReadOnly=true` | `client, sessionId` → `IReadOnlyList<SmPlanItem>` |
| `close_session` | `ReadOnly=false, Destructive=false, Idempotent=true` | `client, sessionId` → `SmSession` |
| `create_plan` | `ReadOnly=false, Destructive=false` | `client, sessionId, desired (SmDesiredState), description, options (SmPlanOptions?)=null` → `SmPlanResult` |
| `get_plan` | `ReadOnly=true` | `client, planId` → `SmPlanDetails` |
| `list_plans` | `ReadOnly=true` | `client, process?=null, sessionId?=null, top=20` → `IReadOnlyList<SmPlan>` |
| `apply_plan` | `ReadOnly=false, Destructive=true, Idempotent=false` | `client, planId, checksum` → `SmApplyResult` |
| `rollback_plan` | `ReadOnly=false, Destructive=false` | `client, planId` → `SmPlanResult` |
| `rollback_session` | `ReadOnly=false, Destructive=false` | `client, sessionId` → `SmPlanResult` |
| `compare_clients` | `ReadOnly=true` | `source, target, process, scope?=null` → `SmPlanResult` |
| `clone_plan` | `ReadOnly=false, Destructive=false` | `source, target, process, sessionId, scope?=null, options?=null` → `SmPlanResult` |
| `legacy_env_migration_plan` | `ReadOnly=false, Destructive=false` | `client, process, sessionId` → `SmPlanResult` |

Descriptions (required content, wording free): `create_plan`, `rollback_plan`, `rollback_session`, `clone_plan`, `legacy_env_migration_plan` — "stores a plan, changes no settings; show the items to the user, then apply_plan after confirmation". `apply_plan` — "the only tool that changes settings; call only after the user confirmed the shown items; status Stale means settings changed since the preview — re-read and rebuild, never partially apply; after Applied tell the user to reload the SPA". `create_plan` documents the desired-state fields, merge vs replace, layers `base`/`env:{E}`/`lang:{L}` (never worker rows), `after` anchors, options. `ServerSetting` values are never available (on read tools already; repeat on `create_plan`).

- [ ] **Step 1: Failing tests** (Integration, client `plan`): `CreatePlan_ThenApply_ViaTools_Applied`; `ApplyPlan_WrongChecksum_ReturnsCodedError` (`Ok == false`, `Error.Code == "checksum_mismatch"`); `CreatePlan_ClosedSession_ReturnsSessionClosed`; `RollbackSession_ViaTools_RestoresValue`; `CompareClients_ViaTools_ReturnsDryRun` (same process in `plan` and `target`, one value changed on `plan`); `Tools_OutdatedClient_ReturnModuleOutdated` (call `start_session` on the `outdated` client → `module_outdated`). In `McpServerSetupTests` (non-integration): all 13 new tool names are registered; `apply_plan` is the only new tool with `Destructive == true`.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~SettingsPlanToolsTests|FullyQualifiedName~McpServerSetupTests"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** same filter → PASS; then `-Mode build`.
- [ ] **Step 5: Commit** `feat(mcp): session, plan, apply, rollback, clone tools`.

---

### Task 12: Plugin skill `manage-settings`

**Files:**
- Create: `plugins/vtmf-settings/skills/manage-settings/SKILL.md`
- Modify: `plugins/vtmf-settings/.claude-plugin/plugin.json` (`"version": "1.1.0"`, description mentions changing settings)
- Modify: `plugins/vtmf-settings/skills/sm-module/SKILL.md` only to add one line: "Then continue with the manage-settings skill for the user's actual task."

**Interfaces:** Frontmatter:
```yaml
---
name: manage-settings
description: Use when the user wants to find, inspect, explain, compare, copy or change VTMF settings of a client or process, or undo a settings change — even if they never mention plans or sessions.
---
```
Body = spec §6 steps 1–12, written as instructions to Claude, plus:
- The preview table format (exact columns): `| # | Action | Table (layer) | Setting | Old → New |`, warnings below in plain words, `ServerSetting` never shown.
- Desired-state cheat sheet: merge vs replace, layer choice, identity of `SettingGroup` rows (`NAME=VALUE` anchors), `after`, when to ask before `createTable`/`targetAlias`.
- Error/status handling table: `Stale` (refresh, explain, rebuild — never partial), `duplicate_row`, `serversetting`, `worker_layer`, `alias_target`, `table_missing`, `session_closed` (start a new session), `checksum_mismatch` (re-create plan), module codes → `sm-module` skill.
- Worked example (barcode): user "Show the barcode settings of spaorderassembling in prod-client1" → `get_settings` (namePrefix `Function.Barcode.`) → explanation → user "When the barcode is not found, ask what to do" → "What are you setting up?" → `start_session` → `create_plan` (merge, `Function.Barcode.Notfound.Action`=`OptionsQuestion`) → preview table → "yes" → `apply_plan` → "Applied. Reload the SPA." → "undo that" → `rollback_plan` → preview → confirm → apply.
- Reply in the user's language; setting names and values verbatim.

- [ ] **Step 1: Write** `SKILL.md` and bump `plugin.json`.
- [ ] **Step 2: Verify manually** (checklist in the commit message body is not needed; run these in a Claude Code session against the dev stack after `docker compose up -d --build vtmf-settings-mcp`, `/plugin marketplace update vtmf`, `/reload-plugins`): (a) "Show the Grid settings of tproc in <dev client>" → no session started, read tools only; (b) "Set Grid.PageSize to 30" → asks for a session name, shows the preview table, does not apply before "yes"; (c) "undo that" → rollback preview, applies after "yes"; (d) a change on a row edited meanwhile in the UI → explains Stale, does not partially apply. Record the outcome in the task report.
- [ ] **Step 3: Commit** `feat(plugin): manage-settings skill (plugin 1.1.0)`.

---

### Task 13: README, task history, gate

**Files:**
- Modify: `README.md` (root)
- Modify: `VTMF.Settings/TASKHISTORY.md`

**Content (root README):**
- §4: tools list adds the 13 phase 2 tools; skills list: `sm-module`, `manage-settings`.
- New subsection **"Updating"** under §4: update both the MCP image (tag in `compose.yaml`, `pull`, `up -d`) and the plugin (`/plugin marketplace update vtmf`, `/reload-plugins`); check versions with `/plugin` (plugin 1.1.0) and `module_status` (module 5); `module_outdated` → the `sm-module` skill installs the new module on next use; `module_newer_than_tool` → update the image.
- New section **"6. Changing settings with Claude"**: you don't ask for a plan — ask to see settings, then say what to change. A short sample conversation (show → change → "What are you setting up?" → preview table → "yes" → applied, reload the SPA → "undo that"). Explain: the preview; "Stale" (someone changed the same settings since the preview — Claude re-reads and shows a new preview; undo never reverts partially); undo of one change vs. "undo everything in this session"; a rolled-back new table is backed up as `bck_{table}_{timestamp}` before it is dropped; every change is recorded with your SQL login; changes made in the Settings UI are not part of sessions.
- §5 sample prompts table gains rows: change a value; add a column to a view (`Add a LOT column after DOCNR in the document list of tproc`); copy a process from client A to B; compare two clients; undo the last change; undo everything I did in this session; show what I changed in this session.
- TASKHISTORY entry (format as the existing one): `## 2026-10-19 — Settings management phase 2: plan engine, sessions, manage-settings skill`, the user's prompts quoted, then `Spec: …; plan: …`.

- [ ] **Step 1: Write** README and TASKHISTORY changes.
- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "Category=Integration"` → all PASS; then `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` → PASS.
- [ ] **Step 3: Commit** `docs: README for phase 2 (changing settings, updating), task history`.
