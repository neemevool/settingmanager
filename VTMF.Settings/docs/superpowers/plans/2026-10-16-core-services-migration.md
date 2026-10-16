# Core Services Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the remaining VTMF.Settings Entities, `SettingTreeNode` and Services into `VTMF.Settings.Core` and make them clean under Core's strict analyzer settings, without changing UI behaviour.

**Architecture:** Files move with `git mv`; namespaces stay the same (`VTMF.Settings.Services`, `VTMF.Settings.Entities`, `VTMF.Settings.Models`). Core gets `AddSettingsCoreServices()`; the UI's `AddSettingsServices()` delegates to it. All analyzer findings are fixed in code.

**Tech Stack:** .NET 10, Dapper 2.1.79, Microsoft.Data.SqlClient 7.1.1, xUnit v2.

**Spec:** `docs/superpowers/specs/2026-10-16-settings-management-tools-design.md` §6 (Core extraction).

**Prerequisite:** phase 1 (`docs/superpowers/plans/2026-10-16-settings-management-phase1.md`) Task 1 is done: `VTMF.Settings.Core` and `VTMF.Settings.Tests` exist, and `AddSqlConnectionParameters()` is in Core.

## Paths used in this plan

| Alias | Path |
|---|---|
| `<root>` | `C:\PROJECTS\VTMF\Settingmanager` (git root) |
| `<ui>` | `<root>\VTMF.Settings` |

## Global Constraints

- Core keeps its `.csproj` settings unchanged: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=Recommended`, `AnalysisModePerformance=All`, `AnalysisModeSecurity=All`. Never relax them to make the move compile.
- No project-wide `NoWarn` and no `.editorconfig` severity downgrades. Suppress only at member/type level with `[SuppressMessage("Category", "CAxxxx:Title", Justification = "...")]`, and only where a fix would change behaviour or SQL.
- Behaviour-preserving only: never change SQL text, public signatures used by the UI, or control flow. Known bugs listed in `docs/settings-architecture.md` §10 (e.g. `GetMaxOrderNr` with a null prefix) stay as they are; fixing them is separate work.
- Build and test only through the wrapper, run from `<root>` (a hook blocks raw `dotnet build` / `dotnet test`): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build | warnings | test | gate`, optionally with `-Target <project>` and `-Filter "<filter>"`.

## Review Focus

1. **Silent behaviour change from an analyzer fix**: string comparison, culture or `ConfigureAwait` changes can alter UI-visible results. Expected: every fix keeps the original semantics. For example, `ToLower()` comparisons become `StringComparison.OrdinalIgnoreCase`, not culture-sensitive comparisons; the reviewer checks each fix's diff against this.
2. **Services that depend on UI types** (Blazor, `NavigationManager`, toast): they must stay in the UI rather than pull ASP.NET Core into Core. The DI test in Step 1 resolves every moved service without a web host.

---

### Task 1: Extract existing Entities and Services into Core

**Files:**
- Move: `<ui>\Entities\*.cs` → `<root>\VTMF.Settings.Core\Entities\`
- Move: `<ui>\Models\SettingTreeNode.cs` → `<root>\VTMF.Settings.Core\Models\SettingTreeNode.cs`
- Move: `<ui>\Services\*.cs` except `ToastServiceExtensions.cs` and `ServiceCollectionExtensions.cs` → `<root>\VTMF.Settings.Core\Services\`
- Create: `<root>\VTMF.Settings.Core\Services\SettingsServiceCollectionExtensions.cs`
- Modify: `<ui>\Services\ServiceCollectionExtensions.cs`
- Test: `<root>\VTMF.Settings.Tests\Core\SettingsServiceRegistrationTests.cs`

**Interfaces:**
- Consumes: `AddSqlConnectionParameters()` (Core, phase 1 Task 1).
- Produces: `IServiceCollection AddSettingsCoreServices(this IServiceCollection services)` (namespace `VTMF.Settings.Services`) registering `ISettingsService, ISettingsMetadataService, IFeatureService, ISchedulerService, IWorkerService, IWikiService, IWikiImageService, IFeatureManagementService` as singletons.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Core\SettingsServiceRegistrationTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VTMF.Settings.Services;

namespace VTMF.Settings.Tests.Core;

public sealed class SettingsServiceRegistrationTests
{
    [Fact]
    public void AddSettingsCoreServices_ResolvesAllServices()
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddLogging();

        services.AddSqlConnectionParameters().AddSettingsCoreServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
        Assert.NotNull(provider.GetRequiredService<ISettingsMetadataService>());
        Assert.NotNull(provider.GetRequiredService<IFeatureService>());
        Assert.NotNull(provider.GetRequiredService<ISchedulerService>());
        Assert.NotNull(provider.GetRequiredService<IWorkerService>());
        Assert.NotNull(provider.GetRequiredService<IWikiService>());
        Assert.NotNull(provider.GetRequiredService<IWikiImageService>());
        Assert.NotNull(provider.GetRequiredService<IFeatureManagementService>());
    }
}
```

If `AddLogging` is not resolvable, run `dotnet add C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings.Tests package Microsoft.Extensions.Logging`.

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~SettingsServiceRegistrationTests"`
Expected: `BUILD FAILED`, because `ISettingsService` / `AddSettingsCoreServices` are not found from Core.

- [ ] **Step 3: Move the files**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
New-Item -ItemType Directory -Force VTMF.Settings.Core\Entities, VTMF.Settings.Core\Models | Out-Null
Get-ChildItem VTMF.Settings\Entities\*.cs | ForEach-Object { git mv $_.FullName "VTMF.Settings.Core\Entities\$($_.Name)" }
git mv VTMF.Settings\Models\SettingTreeNode.cs VTMF.Settings.Core\Models\SettingTreeNode.cs
Get-ChildItem VTMF.Settings\Services\*.cs |
  Where-Object { $_.Name -notin 'ToastServiceExtensions.cs', 'ServiceCollectionExtensions.cs' } |
  ForEach-Object { git mv $_.FullName "VTMF.Settings.Core\Services\$($_.Name)" }
```

`ApiSettingsService.cs.example` stays in the UI project (not compiled). If a moved file turns out to need UI-only types (Review Focus 2), move it back to the UI and report it.

- [ ] **Step 4: Registration extension in Core; UI delegates to it**

`<root>\VTMF.Settings.Core\Services\SettingsServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace VTMF.Settings.Services;

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISettingsMetadataService, SettingMetaService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<IWorkerService, WorkerService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IWikiImageService, WikiImageService>();
        services.AddSingleton<IFeatureManagementService, FeatureManagementService>();
        return services;
    }
}
```

Replace the body of `AddSettingsServices` in `<ui>\Services\ServiceCollectionExtensions.cs` with:

```csharp
        services.AddSqlConnectionParameters();
        services.AddSettingsCoreServices();
        return services;
```

- [ ] **Step 5: Remove UI-only usings from moved files**

Delete these unused directives (they reference ASP.NET Core / UI namespaces Core does not have):
- `Services\SettingsService.cs`: `using Microsoft.AspNetCore.Http.HttpResults;`, `using Microsoft.IdentityModel.Tokens;`, `using VTMF.Settings.Components.Pages;`, `using VTMF.Settings.Components.SettingsManagement;`
- `Entities\ProcessType.cs`: `using Microsoft.AspNetCore.Http.HttpResults;`, `using System.Diagnostics.Metrics;`, `using System.Runtime.Intrinsics.X86;`, `using static System.Net.Mime.MediaTypeNames;`
- `Services\SettingMetaService.cs`: `using System.Reflection.Metadata;`

- [ ] **Step 6: List the analyzer findings in Core**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode warnings -Target VTMF.Settings.Core/VTMF.Settings.Core.csproj`
Because Core uses `TreatWarningsAsErrors`, the findings are reported as errors: the output starts with `BUILD FAILED (...): N error(s)` and lists up to 200 unique lines (`-OutFile` is not written when the build fails). Group them by rule ID and work through one rule at a time.

- [ ] **Step 7: Fix every finding**

Fix all diagnostics; there is no threshold, and the task is done only when Core builds clean. Work rule by rule, behaviour-preserving only:
- Missing types/packages (CS0246) → add the package to Core (`Microsoft.Extensions.Logging.Abstractions` is already referenced).
- Culture/ordinal (CA1304/CA1305/CA1307/CA1309/CA1310/CA1311) → pass the comparison or culture that matches today's runtime behaviour (Review Focus 1).
- Async (CA2007 `ConfigureAwait(false)`, CA1849) → fix in code.
- Static/performance (CA1822, CA1860, CA1862, CA1866, CA1847, CA1859) → fix in code.
- Collections and properties (CA2227, CA1002, CA1819) on Dapper-mapped entities → keep the settable shape Dapper and the UI need; suppress per type with a justification (e.g. `"Dapper materialization and UI binding require a settable collection."`).
- CA2100 on dynamic SQL built from validated table names → `[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Table name validated by regex and wrapped in brackets.")]` on the method. If the table name is **not** validated, do not suppress: report it as a finding instead.
- CA1031 (catch general exception) in existing error paths → keep the behaviour; suppress per member with a justification describing what the catch reports.
- IDE style rules → fix in code.

Re-run Step 6 until it reports `OK (warnings): build succeeded, 0 warnings`. The UI project keeps its own settings; it must also still build (Step 8).

- [ ] **Step 8: Build the solution and run all tests**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` → Expected: `OK (gate)`.
Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj` → Expected: `OK (test)` (Docker running).

- [ ] **Step 9: Smoke-test the UI**

Run: `dotnet run --project C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings --launch-profile http`, open `http://localhost:5045`, then open the client list, one client's Settings Manager page, the Features page and the Scheduler page. Expected: the pages load and show the same data as before the move (requires configured user secrets). Stop the app.

- [ ] **Step 10: Commit and record task history**

Append to `<ui>\TASKHISTORY.md` (per project CLAUDE.md, task content as-is):

```markdown

### Task [015]: Move Entities and Services into VTMF.Settings.Core ✅ COMPLETED

Move the remaining VTMF.Settings Entities, SettingTreeNode and Services into VTMF.Settings.Core under the strict
analyzer settings, following docs/superpowers/plans/2026-10-16-core-services-migration.md.
```

```powershell
git -C C:\PROJECTS\VTMF\Settingmanager add -A VTMF.Settings\Entities VTMF.Settings\Models VTMF.Settings\Services VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "refactor(core): move entities and services into VTMF.Settings.Core"
```

`TASKHISTORY.md` already had uncommitted changes before this work; leave it out of the commit and tell the user it was updated.
