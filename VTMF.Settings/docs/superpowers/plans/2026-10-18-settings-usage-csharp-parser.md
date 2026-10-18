# Settings Usage: C# Parser (SQL Server 2019 floor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `REGEXP_SUBSTR`/`REGEXP_COUNT` from `sm.GetSettingsUsage` so the `sm` module installs and runs on SQL Server 2019+, moving the code scanning and usage assembly into `VTMF.Settings.Core`.

**Architecture:** `sm.GetSettingsUsage` keeps its name and parameters, but it no longer returns usage rows. It returns three raw result sets: the processes in scope, the candidate module definitions, and masked configuration rows. `SmClient.GetSettingsUsageAsync` reads them with `QueryMultipleAsync`. A pure `SettingsUsageScanner` (.NET regex) parses each definition. A pure `SettingsUsageBuilder` reproduces the old procedure's attribution, configuration resolution, filtering and ordering, and returns the unchanged `IReadOnlyList<SmSettingUsage>`. The MCP tool and its output shape do not change.

**Tech Stack:** .NET 10, Dapper, Microsoft.Data.SqlClient, `[GeneratedRegex]`, xUnit, Testcontainers MsSql.

**Spec:** `VTMF.Settings/docs/superpowers/specs/2026-10-16-settings-management-tools-design.md` §5.1 (`sm.GetSettingsUsage` row) and §10. The behavioural contract is the current `060-procedure-GetSettingsUsage.txt` (git `HEAD` of `split-regex-parser` before Task 3) together with `VTMF.Settings.Tests/Sm/SettingsUsageTests.cs`. Both must keep passing unchanged.

## Global Constraints

- The minimum supported server is **SQL Server 2019**, and the client databases run at compatibility level 150. No `sm` script may use 2022+ features (`REGEXP_*`, `GREATEST`/`LEAST`, `DATETRUNC`, `IS DISTINCT FROM`, `json` type, `GENERATE_SERIES`, `STRING_SPLIT` with ordinal).
- Build and test only through `pwsh -NoProfile -File ./eng/verify.ps1 ...`. Never call `dotnet build` or `dotnet test` directly.
- Any change under `VTMF.Settings.Core/Sql/settingsmanagement/` needs two edits in `module.json`:
  - bump `version` from 2 to 3;
  - set `sha256` to the **Expected** value shown by the failing `RecordedHash_MatchesScripts` test (two 32-char halves; concatenate them).
- Scripts stay idempotent (`CREATE OR ALTER`) and contain no `BEGIN TRAN`.
- `ServerSetting` values never leave the database. Masking (`N'********'`) stays in SQL.
- Name comparisons that SQL did under the database collation (process names, setting names, procedure-name containment, `workercode = 'ALL'`) use `StringComparison.OrdinalIgnoreCase` in C#.
- New C# types are `internal` unless `SmClient`'s public surface needs them. The Core project already has `InternalsVisibleTo VTMF.Settings.Tests`.
- The plugin skills do not change, so `plugin.json` is not bumped.
- `sm.UnquoteSqlLiteral` (script 013) becomes unused. It stays in place: removing it would leave it behind on installed databases anyway, and nothing breaks if it is kept.

## Review Focus

1. **Blank `ServerProcessSettings.VALUE`.** SQL treats `N'   '` as equal to `N''` (trailing-space padding). Emptiness is therefore computed in SQL (`isempty`), never with `string.IsNullOrEmpty` in C#. Pinned by the builder test `Build_EmptyValueFromSql_HidesParentForPsv`, with the integration side covered by the existing `Usage_EmptyChildValueHidesParent...`.
2. **Case differences between code and data** (`dbo.getpsv(...)`, `GetProcessSettingValue('TPROC', ...)`, a setting name in different case than the configured row). The old procedure matched all of these. Pinned by `Scan_IsCaseInsensitive` and `Build_MatchesNamesCaseInsensitively`.
3. **Secrets in the new raw result sets.** The procedure now returns configuration rows instead of finished output, so a missing mask would leak `secret-key`. Pinned by the integration test `Procedure_NeverReturnsServerSettingValue`.
4. **`namePrefix` with rows that have no setting name.** The old SQL `LEFT(NULL, n) = @namePrefix` dropped `unparsed` and `direct` rows whenever a prefix was given. Pinned by `Build_NamePrefix_ExcludesRowsWithoutSettingName`.
5. **A legacy call whose process maps elsewhere** (a `SETTINGPROCEDURES` chain: the literal is the parent, and the parent maps on to a third process). The legacy value must be read from the mapped process. This is why the procedure also returns the mapped processes' rows. Pinned by `Build_LegacyCall_ReadsMappedProcessRows`.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `VTMF.Settings.Core/Sm/Usage/SqlLiteral.cs` (create) | C# port of `sm.UnquoteSqlLiteral` |
| `VTMF.Settings.Core/Sm/Usage/SettingsUsageScanner.cs` (create) | Regex scan of one module definition → `SmModuleScan` |
| `VTMF.Settings.Core/Sm/Usage/SmUsageSources.cs` (create) | DTOs for the three result sets plus the `SmUsageSources` bundle |
| `VTMF.Settings.Core/Sm/Usage/SettingsUsageBuilder.cs` (create) | Pure assembly of `SmSettingUsage` rows from the sources |
| `VTMF.Settings.Core/Sm/SmClient.cs` (modify :62-64) | `QueryMultipleAsync` → builder |
| `VTMF.Settings.Core/Sql/settingsmanagement/060-procedure-GetSettingsUsage.txt` (rewrite) | Returns the raw result sets, no regex |
| `VTMF.Settings.Core/Sql/settingsmanagement/module.json` (modify) | version 3 + hash |
| `VTMF.Settings.Tests/Sm/SqlLiteralTests.cs`, `SettingsUsageScannerTests.cs`, `SettingsUsageBuilderTests.cs` (create) | Unit tests (no container) |
| `VTMF.Settings.Tests/Sm/SettingsUsageTests.cs` (modify) | Add the secret-leak integration test |
| `VTMF.Settings.Tests/Sql/SqlServerFixture.cs` (modify :33, :49) | 2019 image |
| Spec §5.1, §10 (modify) | Document the split and the 2019 floor |

---

### Task 1: `SqlLiteral` and `SettingsUsageScanner`

**Files:**
- Create: `VTMF.Settings.Core/Sm/Usage/SqlLiteral.cs`, `VTMF.Settings.Core/Sm/Usage/SettingsUsageScanner.cs`
- Test: `VTMF.Settings.Tests/Sm/SqlLiteralTests.cs`, `VTMF.Settings.Tests/Sm/SettingsUsageScannerTests.cs`

**Interfaces:**
- Produces:
  - `internal static class SqlLiteral { public static string? Unquote(string? s); }`
  - `internal sealed record SmPsvRead(string SettingName, string DefaultRaw);`
  - `internal sealed record SmLegacyRead(string ProcessArg, string SettingName, string DefaultRaw);`
  - `internal sealed record SmModuleScan(string? LiteralProcess, IReadOnlyList<SmPsvRead> PsvReads, IReadOnlyList<SmLegacyRead> LegacyReads, int Unparsed, bool HasDirectRead);`
  - `internal static partial class SettingsUsageScanner { public static SmModuleScan Scan(string definition); }`

- [ ] **Step 1: Write the failing tests**

`SqlLiteralTests` (a `[Theory]` named `Unquote_MatchesSqlFunction`):

| input | expected |
|---|---|
| `'abc'` | `abc` |
| `N'abc'` | `abc` |
| `'it''s'` | `it's` |
| `  'x'  ` | `x` |
| ` 5 ` | `5` |
| `@x` | `@x` |
| `'` | `'` |
| `"5\r\n"` | `5` (deliberate improvement: SQL only trimmed spaces) |
| `null` | `null` |

`SettingsUsageScannerTests`. Use C# raw strings holding T-SQL; the seed bodies come from `TestData/seed.sql:60-96`.
- `Scan_SeedGetDocumentsFortproc`: the body of `GetDocumentsFortproc` →
  - `LiteralProcess == "tproc"`;
  - `PsvReads == [("Document.State.Terminal.New", "'UUS'"), ("Document.Query.Join", "'False'")]`;
  - `Unparsed == 1` (the `GetPSV(@psv, @new, 'x')` call);
  - `HasDirectRead == false`.
- `Scan_SeedRegDocState`: the body of `RegDocStateFortprocFinished` →
  - `LegacyReads == [("'tproc'", "Document.RegDocState.RemoveNoQty", "'False'")]`;
  - `HasDirectRead == true`;
  - `LiteralProcess == null`;
  - `Unparsed == 0`.
- `Scan_LegacyVariableProcessAndTypedVariants`:
  - `dbo.GetProcessSettingValueInt(@p, N'A.B', @w, 5)` → `("@p", "A.B", "5")`;
  - `GetProcessSettingValueDec(` is counted as a call.
- `Scan_PsvDefaultWithEscapedQuoteAndNPrefix`: `dbo.GetPSV(@psv, N'X', N'it''s')` → `("X", "N'it''s'")`.
- `Scan_CallSpanningLines`: an argument list split over `\r\n` still parses.
- `Scan_IsCaseInsensitive`: `dbo.getpsvb(@psv,'A','b')`, `from DBO.serverprocesssettings` and `getprocesssettingvalues(N'TPROC'` all match. `LiteralProcess == "TPROC"`, with the case preserved.
- `Scan_GetProcessSettingValuesIsNotALegacyCall`: a definition containing only `GetProcessSettingValues('tproc', @w)` gives `LegacyReads` empty and `Unparsed == 0`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SqlLiteralTests|FullyQualifiedName~SettingsUsageScannerTests"`
Expected: a build failure, because the types do not exist yet.

- [ ] **Step 3: Implement `SqlLiteral.Unquote` and `SettingsUsageScanner.Scan`**

`Unquote`:
1. Return `null` for `null`.
2. `Trim()`.
3. Strip a leading `N` when the text starts with `N'` and ends with `'` and has a length of at least 3.
4. If the result has a length of at least 2 and starts and ends with `'`, return the inner text with `''` replaced by `'`.
5. Otherwise return the trimmed text.

The scanner uses `[GeneratedRegex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]`. These are the old RE2 patterns with the SQL quote doubling removed; RE2 and .NET both use leftmost-first alternation, so the matches are identical:

```text
Psv        GetPSV[IB]?\s*\(\s*@\w+\s*,\s*N?'([^']+)'\s*,\s*(N?'(?:[^']|'')*'|[^)]*)\)
Legacy     GetProcessSettingValue(?:Int|Dec)?\s*\(\s*(N?'\w+'|@\w+)\s*,\s*N?'([^']+)'\s*,\s*[^,]+,\s*(N?'(?:[^']|'')*'|[^)]*)\)
PsvCall    GetPSV[IB]?\s*\(
LegacyCall GetProcessSettingValue(?:Int|Dec)?\s*\(
Direct     from\s+(?:\[?dbo\]?\.)?\[?ServerProcessSettings\]?
Literal    GetProcessSettingValues\s*\(\s*N?'(\w+)'
```

How each field is computed:
- `Unparsed` = `PsvCall` count + `LegacyCall` count − `Psv` count − `Legacy` count. All counts are non-overlapping `Matches(...).Count`.
- `LiteralProcess` = group 1 of the first `Literal` match, or `null` if there is none.
- `DefaultRaw` and `ProcessArg` are the raw group text; unquoting happens in the builder.

- [ ] **Step 4: Run the tests to verify they pass**

Same command as Step 2. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add VTMF.Settings.Core/Sm/Usage VTMF.Settings.Tests/Sm/SqlLiteralTests.cs VTMF.Settings.Tests/Sm/SettingsUsageScannerTests.cs
git commit -m "feat(core): C# scanner for backend setting reads"
```

---

### Task 2: `SettingsUsageBuilder`

**Files:**
- Create: `VTMF.Settings.Core/Sm/Usage/SmUsageSources.cs`, `VTMF.Settings.Core/Sm/Usage/SettingsUsageBuilder.cs`
- Test: `VTMF.Settings.Tests/Sm/SettingsUsageBuilderTests.cs`

**Interfaces:**
- Consumes: `SettingsUsageScanner.Scan` and `SqlLiteral.Unquote` (Task 1); `SmSettingUsage` (`SmModels.cs:87`, unchanged).
- Produces (these property names match the SQL column names in Task 3, so Dapper binds them):
  - `internal sealed class SmUsageProcess { string Name; string? SettingProcedures; bool InScope; }`: rows for the requested process, its parent (both `InScope = true`), and every process that their `SETTINGPROCEDURES` points to (`InScope = false` unless it is already in scope).
  - `internal sealed class SmUsageModule { string ProcName; string Definition; }`
  - `internal sealed class SmUsageSetting { string Process; string SettingName; string WorkerCode; string? Value; bool IsEmpty; }`: `Value` is already masked.
  - `internal sealed record SmUsageSources(IReadOnlyList<SmUsageProcess> Processes, IReadOnlyList<SmUsageModule> Modules, IReadOnlyList<SmUsageSetting> Settings);`
  - `internal static class SettingsUsageBuilder { public static IReadOnlyList<SmSettingUsage> Build(string process, string? namePrefix, SmUsageSources sources); }`

Rules to implement. Each one mirrors the old procedure; the line numbers refer to the old `060` script.

1. **Scope.** `inScope` = the names of the processes with `InScope`.
2. **Module attribution** (old :54-60):
   - `"literal"` if `scan.LiteralProcess` is in scope;
   - `"procname"` if `LiteralProcess` is null and `ProcName` contains any in-scope name;
   - otherwise `null`.
3. **psv rows** (only for modules whose attribution is not null; old :75-81): `(SettingName, ProcName, "psv", attribution, DefaultRaw)`.
4. **Legacy rows** (computed for every module, independent of module attribution; old :85-108). Per read:
   - `literal` = `null` if `ProcessArg` starts with `@`, otherwise `Unquote(ProcessArg)`;
   - the row's attribution follows rule 2, using this literal instead of the module's;
   - skip the row when that attribution is null;
   - `legacyProcess` = `SettingProcedures` of `(literal ?? process)` from `Processes`, falling back to `(literal ?? process)` itself.
5. **unparsed rows** (attribution not null and `Unparsed > 0`): `(null, ProcName, "unparsed", attribution, Detail = Unparsed.ToString(CultureInfo.InvariantCulture))`.
6. **direct rows** (attribution not null and `HasDirectRead`): `(null, ProcName, "direct", attribution, Detail = "ServerProcessSettings")`.
7. **cfg** (old :129-146):
   - take the settings that have an in-scope process and `WorkerCode` = `ALL`;
   - per `SettingName`, pick the requested process's row first, otherwise the parent's;
   - keep the pick only when `!IsEmpty`;
   - set `ConfiguredIn` to `"process"` or `"parent"`.
8. **workers:** the count of settings where `Process` = the requested process and `WorkerCode` ≠ `ALL`, per `SettingName`.
9. **none rows:** each cfg entry with `ConfiguredIn == "process"` that has no usage row with that `SettingName` gives `(SettingName, null, "none", Unused = true)`.
10. **Configured value columns, non-legacy rows:** `ConfiguredValue`/`ConfiguredIn` come from cfg, and `WorkerOverrides` from workers (0 when missing).
11. **Configured value columns, legacy rows:**
    - `ConfiguredValue` is the first setting with `Process = legacyProcess`, the same name, `ALL` and `!IsEmpty`;
    - `ConfiguredIn` is `"process"` when `legacyProcess` equals the requested process, otherwise `"parent"`, and `null` when there is no such row;
    - `WorkerOverrides` is the count of the same process and name with `WorkerCode` ≠ `ALL`.
12. **`DefaultInCode`** = `Unquote(DefaultRaw)`.
13. **Filter:** when `namePrefix` is not null, keep only rows whose `SettingName` is not null and starts with `namePrefix` (OrdinalIgnoreCase).
14. **Order:**
    1. rows with a null `SettingName` last;
    2. then `SettingName`, then `ProcedureName` (nulls first), then `CallStyle`, all OrdinalIgnoreCase.

All name equality and containment checks use OrdinalIgnoreCase (Global Constraints).

- [ ] **Step 1: Write the failing tests**

Build the `SmUsageSources` inline. A small helper `Module(name, body)` keeps each test short.

- `Build_SeedTproc_MatchesIntegrationContract`:
  - Sources mirror `seed.sql` for `tproc`:
    - processes: `tproc` in scope with `SettingProcedures = null`;
    - the three modules `GetDocumentsFortproc`, `RegDocStateFortprocFinished` and `GetDocumentsFortother`, with bodies copied from the seed;
    - the `tproc.*` settings with `Directo.ApiKey` already masked as `********`.
  - Assert the same 7-tuple sequence as `SettingsUsageTests.Usage_ReportsPsvLegacyDirectUnparsedAndUnused`.
- `Build_EmptyValueFromSql_HidesParentForPsv`:
  - Scope is `tprocvariant` (in scope, `SettingProcedures = "tproc"`) and `tproc` (in scope).
  - Settings: `tprocvariant / Unused.Setting / ALL / "" / IsEmpty = true` and `tproc / Unused.Setting / ALL / "1" / false`.
  - A module with `GetProcessSettingValues('tprocvariant'` and both reads, as in the seed `GetVariantExtras`.
  - Expected rows:
    - `("Unused.Setting", "GetVariantExtras", "legacy", "legacydflt", "1", "parent", 0)`;
    - `("Unused.Setting", "GetVariantExtras", "psv", "dflt", null, null, 0)`.
- `Build_LegacyCall_ReadsMappedProcessRows`:
  - Requested `c`, which maps to parent `b`; `b` maps to `a` (`a` has `InScope = false`).
  - The module calls `GetProcessSettingValue('b', 'S', @w, 'd')`.
  - The settings contain `a / S / ALL / "va"`.
  - Expect a legacy row with `ConfiguredValue = "va"` and `ConfiguredIn = "parent"`.
- `Build_MatchesNamesCaseInsensitively`:
  - The requested process is `"TPROC"` while the processes and settings say `tproc`.
  - The module is `GetDocumentsFortproc` with a `getpsv` call on `'document.query.join'`.
  - Expect attribution `"procname"` and `ConfiguredValue = "True"`.
- `Build_NamePrefix_ExcludesRowsWithoutSettingName`: with the seed sources and `namePrefix = "Document."`, there are no `unparsed` or `direct` rows and every `SettingName` starts with `Document.`.
- `Build_WorkerOverridesCountOnlyRequestedProcessForPsv`:
  - Requested `tprocvariant`, parent `tproc`.
  - The settings include `tproc / Document.Query.Join / worker1`.
  - The psv row for `Document.Query.Join` has `WorkerOverrides == 0`, matching the integration test `Usage_AliasProcess_...`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SettingsUsageBuilderTests"`
Expected: a build failure, because the types do not exist yet.

- [ ] **Step 3: Implement the DTOs and `SettingsUsageBuilder.Build`** following rules 1–14.

- [ ] **Step 4: Run the tests to verify they pass**

Same command as Step 2. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add VTMF.Settings.Core/Sm/Usage VTMF.Settings.Tests/Sm/SettingsUsageBuilderTests.cs
git commit -m "feat(core): assemble settings usage rows in C#"
```

---

### Task 3: Regex-free `sm.GetSettingsUsage` wired to the builder

**Files:**
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/060-procedure-GetSettingsUsage.txt` (full rewrite), `module.json`, `VTMF.Settings.Core/Sm/SmClient.cs:62-64`
- Modify: spec `VTMF.Settings/docs/superpowers/specs/2026-10-16-settings-management-tools-design.md:159`
- Test: `VTMF.Settings.Tests/Sm/SettingsUsageTests.cs` (add one test; the existing four stay unchanged)

**Interfaces:**
- Consumes: `SmUsageProcess`, `SmUsageModule`, `SmUsageSetting`, `SmUsageSources`, `SettingsUsageBuilder.Build` (Task 2).
- Produces: the public signature `SmClient.GetSettingsUsageAsync(string client, string process, string? namePrefix = null, CancellationToken)` (unchanged).

- [ ] **Step 1: Write the failing test**

Add `Procedure_NeverReturnsServerSettingValue` to `SettingsUsageTests`:
1. Open a `SqlConnection` to `fixture.Connections.GetConnectionString(Client)`.
2. Run `EXEC sm.GetSettingsUsage @process = N'tproc'`.
3. Read every result set (`NextResult` loop) and collect all field values as strings.
4. Assert:
   - `Assert.DoesNotContain("secret-key", values)`;
   - `Assert.Contains("********", values)`;
   - `reader` produced exactly 3 result sets.

It fails today because the current procedure returns one result set.

- [ ] **Step 2: Run the test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SettingsUsageTests"`
Expected: `Procedure_NeverReturnsServerSettingValue` FAILS on the result-set count; the other four PASS.

- [ ] **Step 3: Rewrite `060-procedure-GetSettingsUsage.txt`**

Keep:
- the parameters `@process` and `@namePrefix`;
- the validation (51001/51003);
- `@mask`, `@alias` and `#procs`;
- the `#mods` candidate filter (the `LIKE` terms and the exclusion list, old :44-52), minus the `literalprocess` column.

Drop `#tally`, every `REGEXP_*` call, and `#usage`. Return exactly these three result sets, in this order, with these column names:
1. `name, settingprocedures, inscope` (bit): the `#procs` rows (`inscope = 1`) plus every distinct non-null `SETTINGPROCEDURES` of a `#procs` member that is not already in `#procs` (`inscope = 0`). `settingprocedures` is the raw `TOP (1) SETTINGPROCEDURES` from `dbo.processtypes`, with no `NULLIF`.
2. `procname, definition`: from `#mods`.
3. `process, settingname, workercode, value, isempty`, taken from `dbo.ServerProcessSettings` joined on `LEFT(s.NAME, LEN(p.name) + 1) = p.name + N'.'` for each process `p` in result set 1. The columns are:
   - `settingname` = `SUBSTRING(s.NAME, LEN(p.name) + 2, 200)`;
   - `value` = `CASE WHEN s.settingtype = N'ServerSetting' THEN @mask ELSE s.VALUE END`;
   - `isempty` = `CAST(CASE WHEN s.VALUE = N'' THEN 1 ELSE 0 END AS bit)`.

   Filter: `@namePrefix IS NULL OR LEFT(settingname, LEN(@namePrefix)) = @namePrefix`.

- [ ] **Step 4: Rewrite `SmClient.GetSettingsUsageAsync`**

1. Call `EnsureUpToDateAsync`.
2. Run `QueryMultipleAsync` with the same `SqlException` → `SmException` translation as `GetEffectiveSettingsAsync`.
3. Read the three sets into `SmUsageProcess` / `SmUsageModule` / `SmUsageSetting`.
4. Return `SettingsUsageBuilder.Build(process, namePrefix, sources)`.

Dapper matches columns to properties case-insensitively, so the snake-less lowercase names bind.

- [ ] **Step 5: Update the spec**

In the §5.1 row for `sm.GetSettingsUsage`, replace "`REGEXP_*` scan of `sys.sql_modules`" with "returns candidate `sys.sql_modules` definitions plus masked configuration; `SmClient` scans them (.NET regex) and assembles the catalogue". Leave the rest of the row unchanged.

- [ ] **Step 6: Bump the module**

1. In `module.json`, set `"version": 3`.
2. Run `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~RecordedHash_MatchesScripts"`. Expected: FAIL.
3. Concatenate the two Expected halves into `sha256` and rerun. Expected: PASS.

- [ ] **Step 7: Run all usage, module and MCP tests**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SettingsUsage|FullyQualifiedName~SmScriptSet|FullyQualifiedName~SmInstaller|FullyQualifiedName~SettingsReadTools"`
Expected: all PASS. That includes the four original `SettingsUsageTests` with unchanged assertions, plus `Procedure_NeverReturnsServerSettingValue`.

- [ ] **Step 8: Commit**

```bash
git add VTMF.Settings.Core/Sql/settingsmanagement VTMF.Settings.Core/Sm/SmClient.cs VTMF.Settings.Tests/Sm/SettingsUsageTests.cs VTMF.Settings/docs/superpowers/specs
git commit -m "feat(sm): GetSettingsUsage returns raw sources; parsing moves to SmClient (module v3)"
```

---

### Task 4: Test against SQL Server 2019

**Files:**
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs:33` (image) and `:49` (comment)
- Modify: spec `...tools-design.md` §10 line 253, plus one "Minimum server" sentence at the start of §5

- [ ] **Step 1: Switch the image**

1. Set `.WithImage("mcr.microsoft.com/mssql/server:2019-latest")`.
2. Change the comment at :49 to: "Level 150 is the SQL Server 2019 maximum: the supported floor."
3. Keep `ALTER DATABASE smtest SET COMPATIBILITY_LEVEL = 150` and `Usage_RunsAtCompatibilityLevel150` as they are.

- [ ] **Step 2: Run the gate**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate`
Expected: PASS. The first run pulls the 2019 image; allow several minutes.

If a `database5` base script or an `sm` script fails to install on 2019, **stop and report** the script and the error. Do not rewrite base scripts, because they belong to the customer schema and are out of scope.

- [ ] **Step 3: Update the spec**

1. In §10, replace `2025-latest` with `2019-latest (supported floor)`.
2. Add at the start of §5: "`sm` scripts target SQL Server 2019 (compatibility level 150); no 2022+ syntax."

- [ ] **Step 4: Commit**

```bash
git add VTMF.Settings.Tests/Sql/SqlServerFixture.cs VTMF.Settings/docs/superpowers/specs
git commit -m "test: run integration suite on SQL Server 2019, the supported floor"
```
