# Settings management phase 1.1 — `sm` module install & upgrade via MCP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The MCP server can install/upgrade the `sm` module in a client database from scripts shipped in this repo, every other tool refuses unless the module is exactly up to date, and a Claude Code plugin from this repo guides Claude through it.

**Architecture:** The `sm` scripts move into `VTMF.Settings.Core/Sql/settingsmanagement/` with a `module.json` manifest (version + content hash) and are copied to the output/publish folder of every app referencing Core. `SmScriptSet` loads them, `SmInstaller` runs them in one transaction under an app lock and stamps `sm.moduleinfo`, and `SmClient` gates every read on the module state. The MCP server gets an `install_module` tool and server instructions; a plugin under `plugins/vtmf-settings/` carries the skill and MCP connection.

**Tech Stack:** .NET 10, Dapper, Microsoft.Data.SqlClient, ModelContextProtocol C# SDK, xUnit + Testcontainers.MsSql, Docker Compose, Claude Code plugins.

**Spec:** `VTMF.Settings/docs/superpowers/specs/2026-10-17-settings-management-phase1-1-module-install-design.md`

Paths below are relative to the repo root `C:\PROJECTS\VTMF\Settingmanager` unless they start with `<db5>` = `C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5`.

## Global Constraints

- Build/test only via `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build|test|gate`; never `dotnet build`/`dotnet test` directly (a hook blocks it). Integration tests (Docker required): `-Mode test -Filter "Category=Integration"`. Gate runs `Category!=Integration` and must pass before finishing.
- Core/Mcp projects keep `TreatWarningsAsErrors=true` and the analyzer settings; no new warnings.
- Scripts stay idempotent (`IF … IS NULL`, `CREATE OR ALTER`); no grants to role `[worker]`; no `BEGIN TRAN`/`COMMIT` inside scripts.
- Module name in `sm.moduleinfo`: `settingsmanagement`. First version shipped from this repo: **2**.
- `install_module` takes only `client`; it never runs anything but the shipped scripts; never downgrades; ignores `SqlConnectionParameters.InstallMode`.
- Error codes (exact strings): `module_not_installed`, `module_outdated`, `module_newer_than_tool`, `install_failed`, `sm_error`, `unknown_client`.
- Module states (exact strings): `not_installed`, `up_to_date`, `outdated`, `newer_than_tool`.
- Every tool except `list_clients`, `module_status`, `install_module` refuses unless state is `up_to_date`. No exception may escape a tool.
- Do not edit anything under `<db5>` (the user deletes `<db5>\settingsmanagement` after this phase). Do not edit `VTMF.SQLInstaller`.
- The Settings UI does not install `sm`.

## Review Focus

1. **Concurrent `install_module` calls on one database** — both succeed, one `sm.moduleinfo` row, no deadlock error. Test in Task 2 (`Install_ConcurrentCalls_BothSucceed`).
2. **SQL Server unreachable / login fails** — tool returns `sm_error`, server stays up. Test in Task 4 (`ModuleStatus_UnreachableServer_ReturnsSmError`).
3. **Scripts checked out with CRLF on Windows vs LF in the Linux Docker build** — same hash, so the manifest check doesn't flap. Test in Task 1 (`ComputeSha256_IgnoresLineEndings`).
4. **Script folder missing from the published app/image** — fails fast with the path in the message instead of tools failing later. Tests in Task 1 (`Load_MissingDirectory_ThrowsWithPath`, `LoadDefault_ScriptsAreInOutputFolder`) and the Docker check in Task 6.
5. **Database installed from the old database5 copy (stamped 1)** — reported `outdated`, reads refused, `install_module` upgrades to 2. Tests in Tasks 2 and 3 (`upgrade`/`outdated` clients stamped 1).

---

### Task 1: Scripts in this repo + `SmScriptSet` + manifest hash guard

**Files:**
- Create: `VTMF.Settings.Core/Sql/settingsmanagement/*.txt` — copy every file from `<db5>\settingsmanagement\` **except** `999-script-moduleversion.txt` (13 files), content unchanged.
- Create: `VTMF.Settings.Core/Sql/settingsmanagement/module.json`
- Create: `VTMF.Settings.Core/Sm/SmScriptSet.cs`
- Modify: `VTMF.Settings.Core/VTMF.Settings.Core.csproj` (content items)
- Modify: `CLAUDE.md` (repo root) — add a section "sm module scripts"
- Test: `VTMF.Settings.Tests/Sm/SmScriptSetTests.cs` (no Integration trait → runs in gate)

**Interfaces:**
- Produces:
  - `public sealed record SmScript(string Name, string Text);`
  - `public sealed class SmScriptSet` with `public const string ModuleName = "settingsmanagement";`, `int Version`, `string RecordedSha256`, `IReadOnlyList<SmScript> Scripts` (ordinal by file name), `string ComputeSha256()`, `static SmScriptSet Load(string directory)`, `static SmScriptSet LoadDefault()` (directory = `Path.Combine(AppContext.BaseDirectory, "Sql", "settingsmanagement")`), `static string DefaultDirectory`.
  - `module.json` shape: `{ "version": 2, "sha256": "<lowercase hex>" }`.

- [ ] **Step 1: Write the failing tests** in `SmScriptSetTests.cs`:
  - `LoadDefault_ScriptsAreInOutputFolder`: `var set = SmScriptSet.LoadDefault();` → `Assert.Equal(13, set.Scripts.Count)`, `Assert.Equal("001-script-schema.txt", set.Scripts[0].Name)`, `Assert.DoesNotContain(set.Scripts, s => s.Name.StartsWith("999", StringComparison.Ordinal))`, `Assert.Equal(2, set.Version)`.
  - `RecordedHash_MatchesScripts` (the bump guard): `Assert.True(set.RecordedSha256 == set.ComputeSha256(), $"sm scripts changed. Bump \"version\" in Sql/settingsmanagement/module.json and set \"sha256\" to {set.ComputeSha256()}")`.
  - `ComputeSha256_IgnoresLineEndings`: write two temp dirs with the same `module.json` and one script `001-a.txt`, content `"A\r\nB\r\n"` vs `"A\nB\n"` → equal `ComputeSha256()`.
  - `Load_MissingDirectory_ThrowsWithPath`: `Load(<nonexistent path>)` throws `InvalidOperationException` whose message contains the path.
  - `Load_MissingManifest_Throws`: dir with a script and no `module.json` → `InvalidOperationException`.

- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SmScriptSetTests"` — expected: build FAIL (`SmScriptSet` not defined).

- [ ] **Step 3: Implement**
  - Copy the 13 scripts. In the csproj add `<Content Include="Sql\**\*" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` so they flow to Tests, Mcp output and publish.
  - `SmScriptSet.Load`: missing directory or `module.json` → `InvalidOperationException` naming the path; scripts = `*.txt` ordered `StringComparer.Ordinal` by file name, read with `File.ReadAllText` (BOM detection); manifest via `System.Text.Json` (case-insensitive).
  - `ComputeSha256` algorithm (fixed, so the guard is reproducible): for each script in order append `Name + "\n" + Text.Replace("\r\n", "\n") + "\n"`; SHA-256 over UTF-8 bytes; `Convert.ToHexStringLower`.
  - Write `module.json` with `"version": 2` and a placeholder hash, run the guard test once, paste the hash from the failure message.
  - Root `CLAUDE.md` section: "Any change under `VTMF.Settings.Core/Sql/settingsmanagement/` requires bumping `version` in `module.json` and updating `sha256` (the `RecordedHash_MatchesScripts` test prints the new value). Scripts must stay idempotent; no `BEGIN TRAN` in scripts."

- [ ] **Step 4: Run** the Step 2 command — expected: PASS (5 tests).

- [ ] **Step 5: Commit**
```bash
git add VTMF.Settings.Core/Sql VTMF.Settings.Core/Sm/SmScriptSet.cs VTMF.Settings.Core/VTMF.Settings.Core.csproj VTMF.Settings.Tests/Sm/SmScriptSetTests.cs CLAUDE.md
git commit -m "feat(core): sm scripts in repo with versioned manifest and hash guard"
```

---

### Task 2: `SmInstaller` + fixture installs through it

**Files:**
- Create: `VTMF.Settings.Core/Sm/SmInstaller.cs`, `VTMF.Settings.Core/Sm/SmModuleStates.cs`
- Modify: `VTMF.Settings.Core/Sm/SmErrorCodes.cs` (add `InstallFailed`), `VTMF.Settings.Core/Sm/SmModels.cs` (add `SmInstallResult`), `VTMF.Settings.Core/Sm/SmServiceCollectionExtensions.cs`
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs`, `VTMF.Settings.Tests/Sql/Database5Scripts.cs` (remove `SettingsManagementScripts`), `VTMF.Settings.Tests/Mcp/SettingsReadToolsTests.cs` (`ListClients_ReturnsConfiguredClients` compares with `SqlServerFixture.AllClientNames`)
- Test: `VTMF.Settings.Tests/Sm/SmInstallerTests.cs` (Integration)

**Interfaces:**
- Consumes: `SmScriptSet` (Task 1), `SqlScriptParser.SplitIntoBatches(string)` from `VTMF.SQLInstaller.Services`, `SqlConnectionProvider.GetConnection(string)`, `SqlConnectionParameters.DefaultMetaBaseClientName`.
- Produces:
  - `public static class SmModuleStates { NotInstalled="not_installed"; UpToDate="up_to_date"; Outdated="outdated"; NewerThanTool="newer_than_tool"; public static string From(int? installedVersion, int availableVersion); }`
  - `SmErrorCodes.InstallFailed = "install_failed"`, `SmErrorCodes.ModuleOutdated = "module_outdated"`, `SmErrorCodes.ModuleNewerThanTool = "module_newer_than_tool"`
  - `public sealed class SmInstallResult { string Client; string PreviousState; int? PreviousVersion; int InstalledVersion; int ScriptsRun; }` (init properties)
  - `public sealed class SmInstaller(SqlConnectionProvider connections, SmScriptSet scripts)` with `Task<SmInstallResult> InstallAsync(string client, CancellationToken cancellationToken = default)`.
  - `AddSmClient()` also registers `SmScriptSet` (singleton via `SmScriptSet.LoadDefault`) and `SmInstaller` (singleton).
  - Fixture: `SmScriptSet Scripts`, `SmInstaller Installer`, client constants `FreshClientName="fresh"` (base scripts only), `UpgradeClientName="upgrade"` (installed, stamped 1), `ConcurrentClientName="concurrent"` (base only), `BrokenClientName="broken"` (base scripts, then `EXEC(N'CREATE SCHEMA sm')` and `CREATE TABLE sm.GetProcesses (x int)` so script 030 fails deterministically), `static string[] AllClientNames`, helper `Task<T?> ScalarOnAsync<T>(string client, string sql)`. Clients `test` and `upgrade` get `sm` via `Installer.InstallAsync`; stamping uses `UPDATE sm.moduleinfo SET version = 1`.

- [ ] **Step 1: Write the failing tests** in `SmInstallerTests.cs` (`[Trait("Category","Integration")]`, `[Collection(SqlCollection.Name)]`):
  - `Install_FreshDatabase_InstallsCurrentVersion`: result `PreviousState == "not_installed"`, `PreviousVersion == null`, `InstalledVersion == fixture.Scripts.Version`, `ScriptsRun == fixture.Scripts.Scripts.Count`; then `SELECT version FROM sm.moduleinfo WHERE module = N'settingsmanagement'` == `fixture.Scripts.Version`.
  - `Install_OlderVersion_Upgrades` (client `upgrade`): `PreviousState == "outdated"`, `PreviousVersion == 1`, `InstalledVersion == fixture.Scripts.Version`.
  - `Install_UpToDate_RerunsAndKeepsVersion` (client `test`): `PreviousState == "up_to_date"`, `PreviousVersion == InstalledVersion`.
  - `Install_FailingScript_RollsBackAndNamesScript` (client `broken`): `SmException` with `Code == "install_failed"` and message containing `"030-procedure-GetProcesses.txt"`; afterwards `SELECT OBJECT_ID(N'sm.moduleinfo')` is null and `SELECT OBJECT_ID(N'sm.IsSafeName')` is null (earlier scripts rolled back).
  - `Install_ConcurrentCalls_BothSucceed` (client `concurrent`): `Task.WhenAll` of two `InstallAsync`; both return `InstalledVersion == fixture.Scripts.Version`; `SELECT COUNT(*) FROM sm.moduleinfo` == 1.
  - `Install_DefaultMetaBase_ThrowsKeyNotFound`: `InstallAsync(SqlConnectionParameters.DefaultMetaBaseClientName)` throws `KeyNotFoundException`.
  - Newer-than-tool refusal is tested in Task 3 (client `newer`).
  - `ModuleStatesTests` (unit, no trait): `From(null, 2) == not_installed`, `From(1, 2) == outdated`, `From(2, 2) == up_to_date`, `From(3, 2) == newer_than_tool`.

- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SmInstallerTests|FullyQualifiedName~ModuleStatesTests"` — expected: build FAIL (`SmInstaller` not defined).

- [ ] **Step 3: Implement `SmInstaller.InstallAsync`**
  - `client == DefaultMetaBaseClientName` (ordinal-ignore-case) → `KeyNotFoundException` ("not an install target").
  - Open connection, `BeginTransaction()`, `EXEC sp_getapplock @Resource = N'sm.install', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 120000` (result < 0 → `SmException(InstallFailed, "Could not acquire install lock")`).
  - Inside the lock read the installed version on the same connection/transaction: `SELECT CASE WHEN OBJECT_ID(N'sm.moduleinfo', N'U') IS NULL THEN NULL ELSE (SELECT version FROM sm.moduleinfo WHERE module = @module) END` — run as dynamic SQL (`EXEC sp_executesql`) or two statements so it compiles when the table is missing.
  - `SmModuleStates.From(previous, scripts.Version) == NewerThanTool` → rollback, throw `SmException(SmErrorCodes.ModuleNewerThanTool, ...)`.
  - For each script, each batch from `SqlScriptParser.SplitIntoBatches` executed with `CommandTimeout = 300`; `SqlException` → rollback and throw `SmException(InstallFailed, $"Script {script.Name} failed: {ex.Message}", ex)`.
  - Then `MERGE sm.moduleinfo` (same shape as the old `999` script, version from `scripts.Version`), commit, return result.
  - Fixture: install `sm` for `test`/`upgrade` through `Installer`; drop `Database5Scripts.SettingsManagementScripts`; base scripts still from `DATABASE5_PATH`. `Sm` property becomes `new(Connections)` until Task 3 changes the constructor.
  - Update `ModuleInfoTests.GetModuleStatus_InstalledClient_ReturnsVersion` to expect `fixture.Scripts.Version`.

- [ ] **Step 4: Run** the Step 2 command, then `-Mode test -Filter "Category=Integration"` — expected: all PASS (existing phase 1 integration tests still green against the repo scripts).

- [ ] **Step 5: Commit**
```bash
git add VTMF.Settings.Core/Sm VTMF.Settings.Tests
git commit -m "feat(core): SmInstaller runs shipped sm scripts transactionally under an app lock"
```

---

### Task 3: Module state in status + central gate in `SmClient`

**Files:**
- Modify: `VTMF.Settings.Core/Sm/SmClient.cs`, `SmModels.cs` (`SmModuleStatus`), `SmErrorCodes.cs`, `SmException.cs` (message text)
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs` (clients `outdated` stamped 1, `newer` stamped `Scripts.Version + 1`; `Sm => new(Connections, Scripts)`)
- Test: `VTMF.Settings.Tests/Mcp/ModuleGateTests.cs` (Integration), additions to `VTMF.Settings.Tests/Sm/ModuleInfoTests.cs`

**Interfaces:**
- Consumes: `SmScriptSet`, `SmModuleStates.From` (Tasks 1–2).
- Produces:
  - `public sealed class SmClient(SqlConnectionProvider connections, SmScriptSet scripts)` (constructor changes; DI already registers `SmScriptSet`).
  - `SmModuleStatus` gains `int AvailableVersion` and `string State` (init).
  - Uses `SmErrorCodes.ModuleOutdated` / `ModuleNewerThanTool` from Task 2.
  - Gate: every public `SmClient` method except `GetModuleInfoAsync` and `GetModuleStatusAsync` checks state first and throws `SmException` with `module_not_installed` / `module_outdated` / `module_newer_than_tool`.

- [ ] **Step 1: Write the failing tests**
  - `ModuleInfoTests`: `GetModuleStatus_Outdated_ReportsState` (`outdated` client: `State == "outdated"`, `Version == 1`, `AvailableVersion == fixture.Scripts.Version`), `GetModuleStatus_Newer_ReportsState` (`newer_than_tool`), installed `test` → `up_to_date`, `empty` → `not_installed` with `AvailableVersion` set.
  - `ModuleGateTests`: `public static TheoryData<string, string, string>` of (tool name, client, expected code) built from `GatedCalls × {(outdated, module_outdated), (newer, module_newer_than_tool), (empty, module_not_installed)}`, where `GatedCalls` is a `Dictionary<string, Func<SettingsReadTools, string, Task<ToolError?>>>` keyed by MCP tool name: `list_processes`, `list_tables` (`"tproc"`), `get_settings` (`"tproc","DocumentListLayout"`), `get_effective_settings` (`"tproc","DocumentListLayout"`), `settings_usage` (`"tproc"`), `legacy_env_rows` (`"tproc"`). Theory `GatedTool_RefusesUnlessUpToDate`: `Ok == false`, `Error.Code == expected`.
  - `GatedCalls_CoverEveryReadTool` (unit, no trait): reflect over `SettingsReadTools` methods with `McpServerToolAttribute`; the set of `Name`s minus `{list_clients, module_status}` equals `GatedCalls.Keys`.
  - `Install_NewerThanTool_Refuses` in `SmInstallerTests`: `newer` client → `SmException.Code == "module_newer_than_tool"`, version unchanged.

- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "Category=Integration"` — expected: FAIL (outdated/newer currently return data; `State` missing).

- [ ] **Step 3: Implement**
  - `GetModuleStatusAsync` fills `AvailableVersion = scripts.Version`, `State = SmModuleStates.From(Version, scripts.Version)`.
  - Split the private query helper into an ungated one (used by `GetModuleInfoAsync`) and a gated one that first calls `GetModuleStatusAsync` and throws on any state other than `up_to_date`; `GetEffectiveSettingsAsync` uses the same gate. Messages: not installed / outdated → "… call install_module for client '{client}'" with installed and available versions; newer → "… update the VTMF.Settings MCP server image".
  - `SmException.TryTranslate` message for 208/2812 changes to point at `install_module`.

- [ ] **Step 4: Run** the Step 2 command and `-Mode gate` — expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add VTMF.Settings.Core/Sm VTMF.Settings.Tests
git commit -m "feat(core): module state and central version gate in SmClient"
```

---

### Task 4: MCP `install_module`, server instructions, never-crash

**Files:**
- Create: `VTMF.Settings.Mcp/Tools/SettingsModuleTools.cs`, `VTMF.Settings.Mcp/McpServerSetup.cs`
- Modify: `VTMF.Settings.Mcp/Program.cs`, `VTMF.Settings.Mcp/ToolResult.cs`, `VTMF.Settings.Mcp/Tools/SettingsReadTools.cs` (`module_status` description mentions states and `install_module`)
- Modify: `VTMF.Settings.Tests/Sql/SqlServerFixture.cs` (client `unreachable`: `Server=tcp:127.0.0.1,1;Connect Timeout=2;TrustServerCertificate=True`, no database created)
- Test: `VTMF.Settings.Tests/Mcp/SettingsModuleToolsTests.cs` (Integration), `VTMF.Settings.Tests/Mcp/McpServerSetupTests.cs` (unit)

**Interfaces:**
- Consumes: `SmInstaller.InstallAsync`, `SmInstallResult` (Task 2).
- Produces:
  - `[McpServerToolType] public sealed class SettingsModuleTools(SmInstaller installer)` with `[McpServerTool(Name = "install_module", ReadOnly = false, Destructive = false, Idempotent = true)] Task<ToolResult<SmInstallResult>> InstallModule(string client, CancellationToken cancellationToken)`. Description: installs or upgrades the sm module from the scripts shipped with this server; safe to re-run; never downgrades.
  - `public static class McpServerSetup { public const string Instructions = "..."; public static IMcpServerBuilder AddSettingsMcpServer(this IServiceCollection services); }` — calls `AddMcpServer(o => o.ServerInstructions = Instructions)` (verify property name in the SDK version referenced) and registers both tool types. `Program.cs` uses it in both branches, then chains the transport.
  - `ToolResult.RunAsync` also catches `SqlException` → `Failure(SmErrorCodes.SmError, ex.Message)`.
- `Instructions` text (exact):
  > Before working with a client, call module_status. If state is not_installed or outdated, call install_module for that client, then continue. If state is newer_than_tool, stop and tell the user the VTMF.Settings MCP server image must be updated. Tools return errors as { ok: false, error: { code, message } }; codes module_not_installed and module_outdated mean install_module must be run first.

- [ ] **Step 1: Write the failing tests**
  - `InstallModule_FreshClient_ReturnsResult`: use a dedicated fixture client `toolfresh` (base only, add to fixture) → `Ok`, `Data.InstalledVersion == fixture.Scripts.Version`; afterwards `Tools.ListProcesses(toolfresh)` is `Ok`.
  - `InstallModule_Broken_ReturnsInstallFailed`: `broken` → `Ok == false`, `Error.Code == "install_failed"`, message contains `"030-procedure-GetProcesses.txt"`.
  - `InstallModule_Newer_ReturnsNewerThanTool`, `InstallModule_UnknownClient_ReturnsUnknownClient` (`"nosuchclient"`), `InstallModule_DefaultMetaBase_ReturnsUnknownClient`.
  - `ModuleStatus_UnreachableServer_ReturnsSmError` and `ListProcesses_UnreachableServer_ReturnsSmError` (in `SettingsReadToolsTests`): `Ok == false`, `Error.Code == "sm_error"`, no exception.
  - `McpServerSetupTests.AddSettingsMcpServer_SetsInstructions`: build a `ServiceCollection` with `AddSettingsMcpServer()`, resolve `IOptions<McpServerOptions>` → `ServerInstructions == McpServerSetup.Instructions`.

- [ ] **Step 2: Run** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~SettingsModuleToolsTests|FullyQualifiedName~McpServerSetupTests|FullyQualifiedName~Unreachable"` — expected: build FAIL.

- [ ] **Step 3: Implement** per Interfaces.

- [ ] **Step 4: Run** the Step 2 command, then `-Mode test -Filter "Category=Integration"` and `-Mode gate` — expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add VTMF.Settings.Mcp VTMF.Settings.Tests
git commit -m "feat(mcp): install_module tool, server instructions, SQL errors as sm_error"
```

---

### Task 5: Claude Code plugin + skill

**Files:**
- Create: `.claude-plugin/marketplace.json` (repo root), `plugins/vtmf-settings/.claude-plugin/plugin.json`, `plugins/vtmf-settings/.mcp.json`, `plugins/vtmf-settings/skills/sm-module/SKILL.md`
- Modify: `VTMF.Settings/README.md` (MCP section: install plugin, update, URL override, remove any manually added `vtmf-settings` MCP entry to avoid duplicate tools)

**Interfaces:**
- Marketplace name `vtmf`; plugin name `vtmf-settings`, version `1.0.0`, source `./plugins/vtmf-settings`.
- `.mcp.json`: server `vtmf-settings`, `"type": "http"`, `"url": "${VTMF_SETTINGS_MCP_URL:-http://localhost:8081/mcp}"`.
- Install commands for README: `/plugin marketplace add neemevool/settingmanager`, `/plugin install vtmf-settings@vtmf`.

- [ ] **Step 1:** Verify the current plugin/marketplace file layout, `.mcp.json` env-var default syntax and the validate command against Claude Code docs (dispatch a `claude-code-guide` agent). Adjust the file shapes above only if the docs differ.

- [ ] **Step 2:** Write `SKILL.md`. Frontmatter `name: sm-module`, `description:` "Use before working with a VTMF client's settings through the vtmf-settings MCP tools, or when a tool returns module_not_installed, module_outdated or module_newer_than_tool." Body steps:
  1. Client unknown → `list_clients`.
  2. `module_status(client)`.
  3. `not_installed` / `outdated` → `install_module(client)`; report `previousState`/`previousVersion` → `installedVersion`; continue with the original task.
  4. `newer_than_tool` → stop; tell the user to update the MCP server image (`docker compose pull` / new tag in `composeprod.yaml`).
  5. `install_failed` → show the failing script and SQL message; stop; do not retry in a loop.
  6. `sm_error` on every call → server/login unreachable; tell the user, don't install.
  7. Any later tool returning `module_*` → go to step 2.

- [ ] **Step 3:** Write the JSON files and README section.

- [ ] **Step 4: Verify** — run the validate command from Step 1 (e.g. `claude plugin validate .`) — expected: no errors. Then locally `/plugin marketplace add C:\PROJECTS\VTMF\Settingmanager` + `/plugin install vtmf-settings@vtmf` in a scratch session (ask the user to do this if the session can't) and confirm the `sm-module` skill and `vtmf-settings` tools are listed.

- [ ] **Step 5: Commit**
```bash
git add .claude-plugin plugins VTMF.Settings/README.md
git commit -m "feat(plugin): vtmf-settings Claude Code plugin with sm-module skill"
```

---

### Task 6: Compose, installer set cleanup, image check, history

**Files:**
- Modify: `composeprod.yaml`, `VTMF.Settings/install.xml` (remove `<set name="settingsmanagement">`), `VTMF.Settings/README.md` (compose/prod note), `VTMF.Settings/TASKHISTORY.md`

- [ ] **Step 1:** Add to `composeprod.yaml`:
```yaml
  vtmf-settings-mcp:
    image: vtmf/settingsmanager-mcp:v2.16
    ports:
      - "127.0.0.1:8081:8080"
    env_file: settings.env
```
  Remove the `settingsmanagement` set from `VTMF.Settings/install.xml`.

- [ ] **Step 2: Verify image contents**
  Run: `docker build -f Dockerfile.mcp -t vtmf/settingsmanager-mcp:dev .` then `docker run --rm --entrypoint ls vtmf/settingsmanager-mcp:dev /app/Sql/settingsmanagement`
  Expected: 13 `.txt` files and `module.json`. If missing, fix the csproj content item (not the Dockerfile).
  Run: `docker compose -f composeprod.yaml config` — expected: valid, both services listed.

- [ ] **Step 3: Gate** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` and `-Mode test -Filter "Category=Integration"` — expected: PASS.

- [ ] **Step 4:** Append the phase 1.1 task text (the spec's Intent + Goals sections, as-is) to the end of `VTMF.Settings/TASKHISTORY.md` (create the file if it doesn't exist).

- [ ] **Step 5: Commit**
```bash
git add composeprod.yaml VTMF.Settings/install.xml VTMF.Settings/README.md VTMF.Settings/TASKHISTORY.md
git commit -m "chore: prod compose MCP service, drop stale installer set, task history"
```
