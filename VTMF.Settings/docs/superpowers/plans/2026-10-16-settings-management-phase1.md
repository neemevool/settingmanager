# Settings Management Tools — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the read side of the settings-management toolchain: a shared `VTMF.Settings.Core` library, the `sm` read procedures (incl. backend catalogue) in the client DB, a typed `SmClient`, and a `VTMF.Settings.Mcp` server exposing them as MCP tools.

**Architecture:** All rules live in T-SQL procedures in schema `sm` (source in the `database5` repo, deployed by the existing installer). `SmClient` (Core) is a thin Dapper wrapper; the MCP server is a thin tool layer over `SmClient`. VTMF.Settings (UI) references Core and keeps its behaviour unchanged.

**Tech Stack:** .NET 10, Dapper 2.1.79, Microsoft.Data.SqlClient 7.1.1, ModelContextProtocol C# SDK (+ AspNetCore), xUnit v2 + Testcontainers.MsSql (SQL Server 2025 image), T-SQL (SQL Server 2025).

**Spec:** `docs/superpowers/specs/2026-10-16-settings-management-tools-design.md` (read it before starting). Background: `docs/settings-architecture.md`.

## Paths used in this plan

| Alias | Path | Notes |
|---|---|---|
| `<root>` | `C:\PROJECTS\VTMF\Settingmanager` | git repo root (contains `VTMF.Settings\`, `VTMF.SQLInstaller\`, `Dockerfile`, `compose.yaml`) |
| `<ui>` | `<root>\VTMF.Settings` | Blazor UI project + `VTMF.Settings.sln` |
| `<db5>` | `C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5` | separate git repo with all SQL scripts |

## Global Constraints

- Target framework `net10.0` for every new project.
- `VTMF.Settings.Core` and `VTMF.Settings.Mcp` `.csproj` must set exactly: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=Recommended`, `AnalysisModePerformance=All`, `AnalysisModeSecurity=All`.
- No project-wide `NoWarn`. If an analyzer rule cannot reasonably be satisfied, suppress it at member/type level with `[SuppressMessage("Category", "CAxxxx:Title", Justification = "...")]`. Code shown in this plan may need small analyzer-driven adjustments (e.g. `ConfigureAwait`, `CultureInfo`); make them without changing behaviour.
- SQL objects live in schema `sm`; source files in `<db5>\settingsmanagement\` named `###-type-name.txt`; every script is idempotent (`IF ... IS NULL` / `CREATE OR ALTER`); no grants to role `[worker]`.
- Procedure errors use `THROW` numbers 51001–51999: 51001 invalid name, 51002 invalid layer/environment/language, 51003 process not found, 51004 table not found, 51005 layer not supported for table.
- `ServerSetting` values are returned as `********` by every read procedure, never in clear text.
- Layer model: `base` → `{table}`, `workercode='ALL'`; `env:{E}` → `{table}{E}`, `workercode='{E}'`; `lang:{L}` → `{table}{L}`, `workercode='ALL'`. `ServerProcessSettings` supports only `base`.
- Never edit `<db5>\versions.csv` (maintained by that repo's pre-commit hook).
- Existing namespaces `VTMF.Settings.Services`, `VTMF.Settings.Entities`, `VTMF.Settings.Models` are kept when files move to Core. New code uses `VTMF.Settings.Core.Sm` (Core) and `VTMF.Settings.Mcp` (MCP).
- Integration tests need Docker Desktop running (Testcontainers).
- Build and test **only** through the repo wrapper, run from `<root>` (a hook blocks raw `dotnet build` / `dotnet test`): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build`, `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj [-Filter "<filter>"]`, `pwsh -NoProfile -File ./eng/verify.ps1 -Mode warnings -Target <project>`. `dotnet new` / `add` / `sln` / `run` are allowed. An expected "FAIL" in a step means the wrapper prints `BUILD FAILED` or `TESTS FAILED`.
- Every test class that uses the SQL container carries `[Trait("Category", "Integration")]`. The `SubagentStop` gate (`-Mode gate`) runs only `Category!=Integration`, so before finishing a task also run `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj` (all tests, needs Docker) and report its result.

## Review Focus

1. **Client DB at an older compatibility level** (servers are 2025, databases may still be 150): every `sm` procedure must run at compatibility level 150 — the test DB is created at level 150 (Task 2) and the catalogue test (Task 6) exercises `REGEXP_SUBSTR`/`REGEXP_COUNT` there. Do not use `GENERATE_SERIES`, `REGEXP_LIKE`, `REGEXP_MATCHES` or the `json` type in phase 1.
2. **Hostile or malformed identifiers** (`x;drop`, spaces, uppercase/odd layer suffixes, `worker:x`): must raise 51001/51002 before any dynamic SQL — tests in Task 4.
3. **Process with a `SETTINGPROCEDURES` alias and no own tables**: read tools must return the parent's tables / effective values, marked with `owner` / `aliasused` — tests in Tasks 3 and 5.
4. **Secrets**: `ServerSetting` values must be masked in `GetSettings`, `GetEffectiveSettings`, `GetSettingsUsage` and `GetLegacyEnvRows` — a masking assertion exists in each of those tasks.
5. **Client without the `sm` module / unknown client name**: `SmClient` raises `SmException("module_not_installed")`; MCP tools return a structured `{ ok:false, error:{ code } }` with `module_not_installed` / `unknown_client` instead of failing — tests in Tasks 2 and 8.

---

### Task 1: Core library with shared connection configuration

**Files:**
- Create: `<root>\VTMF.Settings.Core\VTMF.Settings.Core.csproj`
- Move: `<ui>\Services\SqlConnectionParameters.cs` → `<root>\VTMF.Settings.Core\Services\SqlConnectionParameters.cs`
- Move: `<ui>\Services\SqlConnectionProvider.cs` → `<root>\VTMF.Settings.Core\Services\SqlConnectionProvider.cs`
- Create: `<root>\VTMF.Settings.Core\Services\SqlConnectionServiceCollectionExtensions.cs`
- Modify: `<ui>\VTMF.Settings.csproj` (add project reference)
- Modify: `<ui>\Services\ServiceCollectionExtensions.cs`
- Modify: `<root>\Dockerfile` (copy Core)
- Create: `<root>\VTMF.Settings.Tests\VTMF.Settings.Tests.csproj`
- Modify: `<root>\eng\verify.config.json` (add the test project to `targets`)
- Test: `<root>\VTMF.Settings.Tests\Core\SqlConnectionParametersTests.cs`

**Interfaces:**
- Produces: `IServiceCollection AddSqlConnectionParameters(this IServiceCollection services)` (namespace `VTMF.Settings.Services`) — registers `Dictionary<string, SqlConnectionParameters>` bound from configuration section `SqlConnectionParameters` and singleton `SqlConnectionProvider`.
- Produces: `SqlConnectionProvider` / `SqlConnectionParameters` (unchanged public API; `DefaultMetaBaseClientName` becomes `const`).

- [ ] **Step 1: Create Core project file**

`<root>\VTMF.Settings.Core\VTMF.Settings.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisMode>Recommended</AnalysisMode>
    <AnalysisModePerformance>All</AnalysisModePerformance>
    <AnalysisModeSecurity>All</AnalysisModeSecurity>
    <RootNamespace>VTMF.Settings</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.1.79" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.1.1" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\VTMF.SQLInstaller\VTMF.SQLInstaller.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="VTMF.Settings.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the test project**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
dotnet new xunit -n VTMF.Settings.Tests -o VTMF.Settings.Tests -f net10.0
dotnet add VTMF.Settings.Tests package xunit --version 2.9.3
dotnet add VTMF.Settings.Tests package Microsoft.Extensions.Configuration
dotnet add VTMF.Settings.Tests package Microsoft.Extensions.DependencyInjection
dotnet add VTMF.Settings.Tests package Testcontainers.MsSql
dotnet add VTMF.Settings.Tests reference VTMF.Settings.Core\VTMF.Settings.Core.csproj VTMF.SQLInstaller\VTMF.SQLInstaller.csproj
dotnet sln VTMF.Settings\VTMF.Settings.sln add VTMF.Settings.Core\VTMF.Settings.Core.csproj VTMF.Settings.Tests\VTMF.Settings.Tests.csproj
Remove-Item VTMF.Settings.Tests\UnitTest1.cs
```

The tests use the xUnit **v2** API (`IAsyncLifetime` with `Task`). If the template produced xUnit v3 packages (`xunit.v3`), replace them with `xunit` 2.9.3 + `xunit.runner.visualstudio`.

Add the test project to `targets` in `<root>\eng\verify.config.json` so the wrapper and the `SubagentStop` gate build and test it (keep the existing entries and order):

```json
  "targets": [
    "VTMF.Settings/VTMF.Settings.csproj",
    "VTMF.SQLInstaller/VTMF.SQLInstaller.csproj",
    "VTMF.Settings.Tests/VTMF.Settings.Tests.csproj"
  ],
```

- [ ] **Step 3: Write the failing test**

`<root>\VTMF.Settings.Tests\Core\SqlConnectionParametersTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VTMF.Settings.Services;

namespace VTMF.Settings.Tests.Core;

public sealed class SqlConnectionParametersTests
{
    [Fact]
    public void ConnectionString_WithCredentials_UsesSqlLogin()
    {
        var parameters = new SqlConnectionParameters { Server = "Server=tcp:db,1433;Database=x", UserId = "u", Password = "p" };

        Assert.Equal("Server=tcp:db,1433;Database=x;User Id=u;Password=p;", parameters.ConnectionString);
    }

    [Fact]
    public void ConnectionString_WithoutCredentials_UsesIntegratedSecurity()
    {
        var parameters = new SqlConnectionParameters { Server = "Server=db" };

        Assert.Equal("Server=db;Integrated Security=True;", parameters.ConnectionString);
    }

    [Fact]
    public void AddSqlConnectionParameters_BindsClientsAndHidesMetabase()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlConnectionParameters:0:ClientName"] = "alpha",
                ["SqlConnectionParameters:0:Server"] = "Server=a",
                ["SqlConnectionParameters:1:ClientName"] = SqlConnectionParameters.DefaultMetaBaseClientName,
                ["SqlConnectionParameters:1:Server"] = "Server=m",
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);

        services.AddSqlConnectionParameters();
        using var provider = services.BuildServiceProvider();

        var connections = provider.GetRequiredService<SqlConnectionProvider>();
        Assert.Equal(["alpha"], connections.GetAvailableClients());
        Assert.Equal("Server=m;Integrated Security=True;", connections.GetConnectionString(SqlConnectionParameters.DefaultMetaBaseClientName));
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj`
Expected: build FAIL — `SqlConnectionParameters` / `AddSqlConnectionParameters` not found (Core is still empty).

- [ ] **Step 5: Move connection classes and add the registration extension**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
New-Item -ItemType Directory -Force VTMF.Settings.Core\Services | Out-Null
git mv VTMF.Settings\Services\SqlConnectionParameters.cs VTMF.Settings.Core\Services\SqlConnectionParameters.cs
git mv VTMF.Settings\Services\SqlConnectionProvider.cs VTMF.Settings.Core\Services\SqlConnectionProvider.cs
```

In `SqlConnectionParameters.cs` change
`public static readonly string DefaultMetaBaseClientName = "DefaultMetaBase";` to
`public const string DefaultMetaBaseClientName = "DefaultMetaBase";`

Create `<root>\VTMF.Settings.Core\Services\SqlConnectionServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VTMF.Settings.Services;

public static class SqlConnectionServiceCollectionExtensions
{
    public const string ConfigurationSection = "SqlConnectionParameters";

    public static IServiceCollection AddSqlConnectionParameters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var parameters = new List<SqlConnectionParameters>();
            configuration.GetSection(ConfigurationSection).Bind(parameters);
            return parameters.ToDictionary(
                p => p.ClientName ?? throw new InvalidOperationException("ClientName is required for SqlConnectionParameters"),
                StringComparer.Ordinal);
        });
        services.AddSingleton<SqlConnectionProvider>();
        return services;
    }
}
```

- [ ] **Step 6: Wire the UI to Core**

```powershell
dotnet add VTMF.Settings\VTMF.Settings.csproj reference VTMF.Settings.Core\VTMF.Settings.Core.csproj
```

In `<ui>\Services\ServiceCollectionExtensions.cs` replace the commented block, the `services.AddSingleton<Dictionary<string, SqlConnectionParameters>>(...)` block and the line `services.AddSingleton<SqlConnectionProvider>();` with a single first statement:

```csharp
        services.AddSqlConnectionParameters();
```

In `<root>\Dockerfile` add after `COPY VTMF.SQLInstaller VTMF.SQLInstaller`:

```dockerfile
COPY VTMF.Settings.Core VTMF.Settings.Core
```

- [ ] **Step 7: Build and fix analyzer findings in Core**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build`
Expected: success. If Core reports analyzer errors in the moved files (e.g. CA1056 for `KeyReceiverUrl`, CA1305), fix them behaviour-preservingly or suppress at member level with a justification (Global Constraints).

- [ ] **Step 8: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj`
Expected: 3 passed.

- [ ] **Step 9: Commit**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
git add VTMF.Settings.Core VTMF.Settings.Tests VTMF.Settings\VTMF.Settings.csproj VTMF.Settings\VTMF.Settings.sln VTMF.Settings\Services Dockerfile eng\verify.config.json
git commit -m "feat(core): extract connection configuration into VTMF.Settings.Core"
```

---

### Task 2: SQL test fixture, `sm` module skeleton, `SmClient` module status

**Files:**
- Create: `<db5>\settingsmanagement\001-script-schema.txt`
- Create: `<db5>\settingsmanagement\010-function-IsSafeName.txt`
- Create: `<db5>\settingsmanagement\011-function-ResolveLayer.txt`
- Create: `<db5>\settingsmanagement\012-function-ProcessTables.txt`
- Create: `<db5>\settingsmanagement\020-procedure-GetModuleInfo.txt`
- Create: `<db5>\settingsmanagement\999-script-moduleversion.txt`
- Create: `<root>\VTMF.Settings.Core\Sm\SmErrorCodes.cs`
- Create: `<root>\VTMF.Settings.Core\Sm\SmException.cs`
- Create: `<root>\VTMF.Settings.Core\Sm\SmModels.cs`
- Create: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Create: `<root>\VTMF.Settings.Core\Sm\SmServiceCollectionExtensions.cs`
- Create: `<root>\VTMF.Settings.Tests\Sql\Database5Scripts.cs`
- Create: `<root>\VTMF.Settings.Tests\Sql\SqlServerFixture.cs`
- Create: `<root>\VTMF.Settings.Tests\TestData\seed.sql`
- Modify: `<root>\VTMF.Settings.Tests\VTMF.Settings.Tests.csproj` (copy TestData)
- Test: `<root>\VTMF.Settings.Tests\Sm\ModuleInfoTests.cs`

**Interfaces:**
- Produces (SQL): `sm.IsSafeName(@name nvarchar(200)) RETURNS bit`; `sm.ResolveLayer(@layer nvarchar(110))` → `(kind, suffix, workercode)`; `sm.ProcessTables(@process nvarchar(100))` → `(physicaltable, basetable, view, layer)`; `sm.GetModuleInfo` → `(module, version, installed)`.
- Produces (C#, namespace `VTMF.Settings.Core.Sm`):
  - `public sealed class SmClient(SqlConnectionProvider connections)` with `Task<SmModuleInfo?> GetModuleInfoAsync(string client, CancellationToken cancellationToken = default)` and `Task<SmModuleStatus> GetModuleStatusAsync(string client, CancellationToken cancellationToken = default)`.
  - `SmException : Exception` with `string Code`; codes in `SmErrorCodes` (`module_not_installed`, `invalid_name`, `invalid_layer`, `process_not_found`, `table_not_found`, `layer_not_supported`, `sm_error`).
  - `IServiceCollection AddSmClient(this IServiceCollection services)`.
  - Test fixture `SqlServerFixture` with `const string ClientName = "test"`, `const string EmptyClientName = "empty"`, properties `SqlConnectionProvider Connections`, `SmClient Sm`; collection name `SqlCollection.Name = "sql"`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\ModuleInfoTests.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ModuleInfoTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetModuleStatus_InstalledClient_ReturnsVersion()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.ClientName);

        Assert.True(status.Installed);
        Assert.Equal(1, status.Version);
    }

    [Fact]
    public async Task GetModuleStatus_ClientWithoutModule_ReportsNotInstalled()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.EmptyClientName);

        Assert.False(status.Installed);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetModuleInfo_ClientWithoutModule_ThrowsModuleNotInstalled()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetModuleInfoAsync(SqlServerFixture.EmptyClientName));

        Assert.Equal(SmErrorCodes.ModuleNotInstalled, ex.Code);
    }
}
```

- [ ] **Step 2: Write the fixture, script list and seed**

`<root>\VTMF.Settings.Tests\Sql\Database5Scripts.cs`:

```csharp
namespace VTMF.Settings.Tests.Sql;

internal static class Database5Scripts
{
    public static string Root =>
        Environment.GetEnvironmentVariable("DATABASE5_PATH") ?? @"C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5";

    // Minimal subset of the "initialdatabase"/"base" sets the sm module depends on, in dependency order.
    public static IEnumerable<string> BaseScripts =>
    [
        Path.Combine(Root, "init", "001-script-worker.txt"),
        Path.Combine(Root, "worker", "002-table-processtypes.txt"),
        Path.Combine(Root, "settings", "024-table-ServerProcessSettings.txt"),
        Path.Combine(Root, "settings", "034-script-processSettingstype.txt"),
        Path.Combine(Root, "settings", "025-function-GetProcessSettingValue.txt"),
        Path.Combine(Root, "settings", "035-function-GetProcessSettingValues.txt"),
        Path.Combine(Root, "settings", "036-function-GetPSV.txt"),
        Path.Combine(Root, "settings", "037-function-GetPSVI.txt"),
        Path.Combine(Root, "settings", "038-function-GetPSVB.txt"),
    ];

    public static IEnumerable<string> SettingsManagementScripts =>
        Directory.GetFiles(Path.Combine(Root, "settingsmanagement"), "*.txt")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);
}
```

`<root>\VTMF.Settings.Tests\Sql\SqlServerFixture.cs`:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Services;
using VTMF.SQLInstaller.Services;

namespace VTMF.Settings.Tests.Sql;

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ClientName = "test";
    public const string EmptyClientName = "empty";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
        .Build();

    public SqlConnectionProvider Connections { get; private set; } = null!;

    public SmClient Sm => new(Connections);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var master = _container.GetConnectionString();

        // Client databases may still run at an older compatibility level after the server upgrade.
        await ExecuteAsync(master, "CREATE DATABASE smtest; ALTER DATABASE smtest SET COMPATIBILITY_LEVEL = 150; CREATE DATABASE smempty;");

        var builder = new SqlConnectionStringBuilder(master);
        Connections = new SqlConnectionProvider(new Dictionary<string, SqlConnectionParameters>
        {
            [ClientName] = Parameters(builder, "smtest", ClientName),
            [EmptyClientName] = Parameters(builder, "smempty", EmptyClientName),
        });

        var test = Connections.GetConnectionString(ClientName);
        foreach (var script in Database5Scripts.BaseScripts.Concat(Database5Scripts.SettingsManagementScripts))
            await RunScriptFileAsync(test, script);
        await RunScriptFileAsync(test, Path.Combine(AppContext.BaseDirectory, "TestData", "seed.sql"));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public Task ExecuteOnTestAsync(string sql) => ExecuteAsync(Connections.GetConnectionString(ClientName), sql);

    private static SqlConnectionParameters Parameters(SqlConnectionStringBuilder master, string database, string client) => new()
    {
        ClientName = client,
        Server = $"Server={master.DataSource};Database={database};TrustServerCertificate=True",
        UserId = master.UserID,
        Password = master.Password,
    };

    private static async Task RunScriptFileAsync(string connectionString, string path)
    {
        var text = await File.ReadAllTextAsync(path); // detects UTF-16 BOMs used by some database5 files
        var parser = new SqlScriptParser(NullLogger<SqlScriptParser>.Instance);
        foreach (var batch in parser.SplitIntoBatches(text))
        {
            try
            {
                await ExecuteAsync(connectionString, batch);
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"Script {path} failed: {ex.Message}", ex);
            }
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
```

`<root>\VTMF.Settings.Tests\TestData\seed.sql` (used by all later tasks; `GO` separates batches):

```sql
INSERT INTO dbo.processtypes (NAME, DISPLAYLABEL, TAG, PROCESSTYPE, DATAPROCEDURES, SETTINGPROCEDURES, ENVIRONMENT, ORDERNR)
VALUES (N'tproc', N'Test process', N'test', N'TaskDocument', NULL, NULL, N'wterminal', 10),
       (N'tprocvariant', N'Test variant', N'test', N'TaskDocument', N'tproc', N'tproc', N'wterminal', 20),
       (N'tother', N'Other process', N'test', N'RelocateItems', NULL, NULL, N'wterminal', 30);
GO

CREATE TABLE dbo.tprocProcessSettings (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayout (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayoutwterminal2 (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayouten (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentItemsListLayout (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.totherProcessSettings (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
GO

INSERT INTO dbo.tprocProcessSettings VALUES (N'Function.Barcode.Parser', N'Parserbuilder', N'Setting', NULL, N'ALL', 10);
INSERT INTO dbo.tprocDocumentItemsListLayout VALUES (N'Column', N'QTY', N'SettingGroup', NULL, N'ALL', 10);
INSERT INTO dbo.totherProcessSettings VALUES (N'Function.OnStart', N'Documents', N'Setting', NULL, N'ALL', 10);

INSERT INTO dbo.tprocDocumentListLayout (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column',               N'DOCNR',    N'SettingGroup',  NULL, N'ALL',        10),
 (N'Column.DOCNR.Caption', N'Dok nr',   N'Setting',       NULL, N'ALL',        20),
 (N'Column',               N'DATE',     N'SettingGroup',  NULL, N'ALL',        30),
 (N'Column.DATE.Caption',  N'Kuupäev',  N'Setting',       NULL, N'ALL',        40),
 (N'Grid.PageSize',        N'20',       N'Setting',       NULL, N'ALL',        50),
 (N'Grid.Empty',           N'',         N'Setting',       NULL, N'ALL',        60),
 (N'Grid.Secret',          N'abc',      N'ServerSetting', NULL, N'ALL',        70),
 (N'Column.DOCNR.Caption', N'Legacy',   N'Setting',       NULL, N'wterminal2', 20);

INSERT INTO dbo.tprocDocumentListLayoutwterminal2 (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column.DOCNR.Caption', N'Doc no (env)', N'Setting',      NULL, N'wterminal2', 10),
 (N'Column',               N'STATUS',       N'SettingGroup', NULL, N'wterminal2', 20),
 (N'Column',               N'DOCNR',        N'SettingGroup', NULL, N'wterminal2', 25),
 (N'Grid.ShowFilterRow',   N'true',         N'Setting',      NULL, N'wterminal2', 30),
 (N'Ignored.AllRow',       N'x',            N'Setting',      NULL, N'ALL',        40);

INSERT INTO dbo.tprocDocumentListLayouten (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column.DATE.Caption', N'Date',  N'Setting', NULL, N'ALL', 10),
 (N'Grid.ShowFilterRow',  N'false', N'Setting', NULL, N'ALL', 20),
 (N'Lang.Only',           N'x',     N'Setting', NULL, N'ALL', 30);

INSERT INTO dbo.ServerProcessSettings (NAME, VALUE, workercode, ordernr, settingtype, LISTVALUE) VALUES
 (N'tproc.Document.State.Terminal.New',        N'NEW',        N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Document.Query.Join',                N'True',       N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Document.Query.Join',                N'False',      N'worker1', 0, N'Setting',       NULL),
 (N'tproc.Unused.Setting',                     N'1',          N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Directo.ApiKey',                     N'secret-key', N'ALL',     0, N'ServerSetting', NULL),
 (N'tprocvariant.Document.State.Terminal.New', N'VNEW',       N'ALL',     0, N'Setting',       NULL),
 (N'tother.Other.Setting',                     N'o',          N'ALL',     0, N'Setting',       NULL);
GO

CREATE PROCEDURE dbo.GettprocDocumentItemsListLayoutInfo @workercode nvarchar(250), @objectname nvarchar(100), @objectkey nvarchar(100)
AS SELECT 1 AS x;
GO

CREATE PROCEDURE dbo.[GettprocProcessSettingsInfo-] @workercode nvarchar(250)
AS SELECT 1 AS x;
GO

CREATE PROCEDURE dbo.GetDocumentsFortproc @workercode nvarchar(250)
AS
BEGIN
    declare @psv dbo.processSettings
    insert into @psv select * from dbo.GetProcessSettingValues('tproc', @workercode)
    declare @new nvarchar(30) = dbo.GetPSV(@psv, 'Document.State.Terminal.New', 'UUS')
    declare @join nvarchar(10) = dbo.GetPSVB(@psv,'Document.Query.Join','False')
    declare @dyn nvarchar(30) = dbo.GetPSV(@psv, @new, 'x')
    select @new, @join, @dyn
END
GO

CREATE PROCEDURE dbo.RegDocStateFortprocFinished @workercode nvarchar(250)
AS
BEGIN
    declare @remove nvarchar(10) = dbo.GetProcessSettingValue('tproc','Document.RegDocState.RemoveNoQty',@workercode,'False')
    declare @key nvarchar(400) = (select top 1 VALUE from dbo.ServerProcessSettings where NAME = 'tproc.Directo.ApiKey')
    select @remove, @key
END
GO

CREATE PROCEDURE dbo.GetDocumentsFortother @workercode nvarchar(250)
AS
BEGIN
    declare @psv dbo.processSettings
    insert into @psv select * from dbo.GetProcessSettingValues('tother', @workercode)
    select dbo.GetPSV(@psv, 'Other.Setting', 'o')
END
GO
```

If the `processtypes` insert fails because the installed DDL has additional `NOT NULL` columns without defaults, add those columns with neutral values to the insert (do not change the database5 DDL).

Add to `VTMF.Settings.Tests.csproj`:

```xml
  <ItemGroup>
    <None Update="TestData\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~ModuleInfoTests"`
Expected: build FAIL — `SmClient`, `SmException`, `SmErrorCodes` not found.

- [ ] **Step 4: Write the SQL module skeleton**

`<db5>\settingsmanagement\001-script-schema.txt`:

```sql
IF SCHEMA_ID(N'sm') IS NULL
    EXEC (N'CREATE SCHEMA sm');
GO

IF OBJECT_ID(N'sm.moduleinfo', N'U') IS NULL
BEGIN
    CREATE TABLE sm.moduleinfo
    (
        module    nvarchar(50) NOT NULL CONSTRAINT PK_sm_moduleinfo PRIMARY KEY,
        version   int          NOT NULL,
        installed datetime2(0) NOT NULL CONSTRAINT DF_sm_moduleinfo_installed DEFAULT (SYSUTCDATETIME())
    );
END
GO
```

`<db5>\settingsmanagement\010-function-IsSafeName.txt`:

```sql
CREATE OR ALTER FUNCTION sm.IsSafeName(@name nvarchar(200))
RETURNS bit
AS
BEGIN
    -- Identifiers that end up in dynamic SQL: letters, digits and underscore only.
    RETURN CASE
               WHEN @name IS NOT NULL
                    AND LEN(@name) BETWEEN 1 AND 128
                    AND @name COLLATE Latin1_General_BIN NOT LIKE N'%[^A-Za-z0-9_]%'
               THEN CAST(1 AS bit)
               ELSE CAST(0 AS bit)
           END;
END
GO
```

`<db5>\settingsmanagement\011-function-ResolveLayer.txt`:

```sql
CREATE OR ALTER FUNCTION sm.ResolveLayer(@layer nvarchar(110))
RETURNS TABLE
AS
RETURN
    SELECT x.kind,
           x.suffix,
           CASE x.kind WHEN N'env' THEN x.suffix ELSE N'ALL' END AS workercode
    FROM (SELECT CASE
                     WHEN @layer = N'base' THEN N'base'
                     WHEN @layer LIKE N'env:_%'
                          AND SUBSTRING(@layer, 5, 100) COLLATE Latin1_General_BIN NOT LIKE N'%[^a-z0-9_]%' THEN N'env'
                     WHEN @layer LIKE N'lang:_%'
                          AND SUBSTRING(@layer, 6, 100) COLLATE Latin1_General_BIN NOT LIKE N'%[^a-z0-9_]%' THEN N'lang'
                 END AS kind,
                 CASE
                     WHEN @layer LIKE N'env:_%' THEN SUBSTRING(@layer, 5, 100)
                     WHEN @layer LIKE N'lang:_%' THEN SUBSTRING(@layer, 6, 100)
                     ELSE N''
                 END AS suffix) AS x;
GO
```

`<db5>\settingsmanagement\012-function-ProcessTables.txt`:

```sql
-- Settings tables owned by a process: dbo tables with a settingtype column named {process}{View}
-- (View starts with an uppercase letter). A table whose name is another such table plus a
-- lowercase suffix is an overlay: 2-character suffix = language, otherwise environment.
CREATE OR ALTER FUNCTION sm.ProcessTables(@process nvarchar(100))
RETURNS TABLE
AS
RETURN
WITH cand AS
(
    SELECT t.TABLE_NAME AS physicaltable
    FROM INFORMATION_SCHEMA.TABLES AS t
    WHERE t.TABLE_SCHEMA = N'dbo'
      AND t.TABLE_TYPE = N'BASE TABLE'
      AND LEFT(t.TABLE_NAME, LEN(@process)) = @process
      AND SUBSTRING(t.TABLE_NAME, LEN(@process) + 1, 1) COLLATE Latin1_General_BIN LIKE N'[A-Z]'
      AND EXISTS (SELECT 1
                  FROM INFORMATION_SCHEMA.COLUMNS AS c
                  WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA
                    AND c.TABLE_NAME = t.TABLE_NAME
                    AND c.COLUMN_NAME = N'settingtype')
),
classified AS
(
    SELECT c.physicaltable,
           b.physicaltable AS basetable,
           ROW_NUMBER() OVER (PARTITION BY c.physicaltable ORDER BY LEN(b.physicaltable) DESC) AS rn
    FROM cand AS c
    LEFT JOIN cand AS b
           ON LEN(c.physicaltable) > LEN(b.physicaltable)
          AND LEFT(c.physicaltable, LEN(b.physicaltable)) = b.physicaltable
          AND SUBSTRING(c.physicaltable, LEN(b.physicaltable) + 1, 200) COLLATE Latin1_General_BIN NOT LIKE N'%[^a-z0-9_]%'
)
SELECT physicaltable,
       COALESCE(basetable, physicaltable) AS basetable,
       SUBSTRING(COALESCE(basetable, physicaltable), LEN(@process) + 1, 200) AS [view],
       CASE
           WHEN basetable IS NULL THEN N'base'
           WHEN LEN(physicaltable) - LEN(basetable) = 2 THEN N'lang:' + SUBSTRING(physicaltable, LEN(basetable) + 1, 200)
           ELSE N'env:' + SUBSTRING(physicaltable, LEN(basetable) + 1, 200)
       END AS layer
FROM classified
WHERE rn = 1;
GO
```

`<db5>\settingsmanagement\020-procedure-GetModuleInfo.txt`:

```sql
CREATE OR ALTER PROCEDURE sm.GetModuleInfo
AS
BEGIN
    SET NOCOUNT ON;
    SELECT module, version, installed FROM sm.moduleinfo;
END
GO
```

`<db5>\settingsmanagement\999-script-moduleversion.txt`:

```sql
-- Runs last: records the module version after all objects exist. Bump when the module changes.
MERGE sm.moduleinfo AS t
USING (SELECT N'settingsmanagement' AS module, 1 AS version) AS s
   ON t.module = s.module
WHEN MATCHED THEN UPDATE SET version = s.version, installed = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (module, version) VALUES (s.module, s.version);
GO
```

- [ ] **Step 5: Write the C# side**

`<root>\VTMF.Settings.Core\Sm\SmErrorCodes.cs`:

```csharp
namespace VTMF.Settings.Core.Sm;

public static class SmErrorCodes
{
    public const string ModuleNotInstalled = "module_not_installed";
    public const string InvalidName = "invalid_name";
    public const string InvalidLayer = "invalid_layer";
    public const string ProcessNotFound = "process_not_found";
    public const string TableNotFound = "table_not_found";
    public const string LayerNotSupported = "layer_not_supported";
    public const string SmError = "sm_error";

    internal static string FromSqlErrorNumber(int number) => number switch
    {
        51001 => InvalidName,
        51002 => InvalidLayer,
        51003 => ProcessNotFound,
        51004 => TableNotFound,
        51005 => LayerNotSupported,
        _ => SmError,
    };
}
```

`<root>\VTMF.Settings.Core\Sm\SmException.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace VTMF.Settings.Core.Sm;

public sealed class SmException : Exception
{
    private const int CouldNotFindStoredProcedure = 2812;

    public SmException()
        : this(SmErrorCodes.SmError, "Settings management error.")
    {
    }

    public SmException(string message)
        : this(SmErrorCodes.SmError, message)
    {
    }

    public SmException(string message, Exception innerException)
        : this(SmErrorCodes.SmError, message, innerException)
    {
    }

    public SmException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    internal static bool TryTranslate(SqlException exception, out SmException translated)
    {
        if (exception.Number == CouldNotFindStoredProcedure && exception.Message.Contains("'sm.", StringComparison.OrdinalIgnoreCase))
        {
            translated = new SmException(SmErrorCodes.ModuleNotInstalled,
                "The settings management module (sm) is not installed in this client database. Install set 'settingsmanagement' with the VTMF.Settings installer.",
                exception);
            return true;
        }

        if (exception.Number is >= 51000 and <= 51999)
        {
            translated = new SmException(SmErrorCodes.FromSqlErrorNumber(exception.Number), exception.Message, exception);
            return true;
        }

        translated = null!;
        return false;
    }
}
```

`<root>\VTMF.Settings.Core\Sm\SmModels.cs` (all DTOs for phase 1; later tasks only consume them):

```csharp
namespace VTMF.Settings.Core.Sm;

public sealed class SmModuleInfo
{
    public string Module { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime Installed { get; set; }
}

public sealed class SmModuleStatus
{
    public bool Installed { get; init; }
    public int? Version { get; init; }
}

public sealed class SmProcess
{
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string ProcessType { get; set; } = string.Empty;
    public string? DataProcedures { get; set; }
    public string? SettingProcedures { get; set; }
    public string? Environment { get; set; }
    public int? OrderNr { get; set; }
    public string? Variants { get; set; }
    public int TableCount { get; set; }
}

public sealed class SmTable
{
    public string View { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string PhysicalTable { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string? OverrideProcedure { get; set; }
    public bool OverrideActive { get; set; }
}

public sealed class SmSettingRow
{
    public string Table { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
}

public sealed class SmEffectiveInfo
{
    public string? PhysicalTable { get; set; }
    public bool AliasUsed { get; set; }
    public string? OverrideProcedure { get; set; }
    public string? AdditionalProcedure { get; set; }
}

public sealed class SmEffectiveRow
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class SmEffectiveSettings
{
    public required SmEffectiveInfo Info { get; init; }
    public required IReadOnlyList<SmEffectiveRow> Rows { get; init; }
}

public sealed class SmSettingUsage
{
    public string? SettingName { get; set; }
    public string? ProcedureName { get; set; }
    public string CallStyle { get; set; } = string.Empty;
    public string? Attribution { get; set; }
    public string? DefaultInCode { get; set; }
    public string? ConfiguredValue { get; set; }
    public string? ConfiguredIn { get; set; }
    public int WorkerOverrides { get; set; }
    public bool Unused { get; set; }
    public string? Detail { get; set; }
}

public sealed class SmLegacyEnvRow
{
    public string PhysicalTable { get; set; } = string.Empty;
    public string View { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
    public bool OverlayExists { get; set; }
}
```

`<root>\VTMF.Settings.Core\Sm\SmClient.cs`:

```csharp
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>Typed access to the sm.* procedures of a client database. Holds no business rules.</summary>
public sealed class SmClient(SqlConnectionProvider connections)
{
    public async Task<SmModuleInfo?> GetModuleInfoAsync(string client, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<SmModuleInfo>(client, "sm.GetModuleInfo", null, cancellationToken).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<SmModuleStatus> GetModuleStatusAsync(string client, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetModuleInfoAsync(client, cancellationToken).ConfigureAwait(false);
            return new SmModuleStatus { Installed = info is not null, Version = info?.Version };
        }
        catch (SmException ex) when (ex.Code == SmErrorCodes.ModuleNotInstalled)
        {
            return new SmModuleStatus { Installed = false, Version = null };
        }
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string client, string procedure, object? parameters, CancellationToken cancellationToken)
    {
        await using var connection = connections.GetConnection(client);
        try
        {
            var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<T>(command).ConfigureAwait(false);
            return rows.AsList();
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }
}
```

`<root>\VTMF.Settings.Core\Sm\SmServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace VTMF.Settings.Core.Sm;

public static class SmServiceCollectionExtensions
{
    public static IServiceCollection AddSmClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SmClient>();
        return services;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~ModuleInfoTests"`
Expected: 3 passed (first run pulls the SQL Server 2025 image; allow several minutes).

- [ ] **Step 7: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: sm schema skeleton, helpers, GetModuleInfo"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient module status and SQL Server test fixture"
```

---

### Task 3: `sm.GetProcesses` and `sm.GetTables`

**Files:**
- Create: `<db5>\settingsmanagement\030-procedure-GetProcesses.txt`
- Create: `<db5>\settingsmanagement\031-procedure-GetTables.txt`
- Modify: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Test: `<root>\VTMF.Settings.Tests\Sm\ProcessAndTableTests.cs`

**Interfaces:**
- Consumes: `sm.IsSafeName`, `sm.ProcessTables`, `SmClient.QueryAsync<T>`, `SmProcess`, `SmTable`.
- Produces: `Task<IReadOnlyList<SmProcess>> GetProcessesAsync(string client, CancellationToken cancellationToken = default)`; `Task<IReadOnlyList<SmTable>> GetTablesAsync(string client, string process, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\ProcessAndTableTests.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ProcessAndTableTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetProcesses_ReturnsAliasVariantsAndTableCount()
    {
        var processes = await fixture.Sm.GetProcessesAsync(SqlServerFixture.ClientName);

        var tproc = Assert.Single(processes, p => p.Name == "tproc");
        Assert.Equal("TaskDocument", tproc.ProcessType);
        Assert.Equal("tprocvariant", tproc.Variants);
        Assert.Equal(5, tproc.TableCount);
        var variant = Assert.Single(processes, p => p.Name == "tprocvariant");
        Assert.Equal("tproc", variant.SettingProcedures);
        Assert.Equal(0, variant.TableCount);
    }

    [Fact]
    public async Task GetTables_ClassifiesLayersOverridesAndBackendTable()
    {
        var tables = await fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "tproc");

        Assert.Equal(
            [
                ("DocumentItemsListLayout", "base", "tprocDocumentItemsListLayout", 1, "GettprocDocumentItemsListLayoutInfo", true),
                ("DocumentListLayout", "base", "tprocDocumentListLayout", 8, null, false),
                ("DocumentListLayout", "env:wterminal2", "tprocDocumentListLayoutwterminal2", 5, null, false),
                ("DocumentListLayout", "lang:en", "tprocDocumentListLayouten", 3, null, false),
                ("ProcessSettings", "base", "tprocProcessSettings", 1, "GettprocProcessSettingsInfo-", false),
                ("ServerProcessSettings", "base", "ServerProcessSettings", 5, null, false),
            ],
            tables.Select(t => (t.View, t.Layer, t.PhysicalTable, t.RowCount, t.OverrideProcedure, t.OverrideActive)).ToArray());
        Assert.All(tables, t => Assert.Equal("tproc", t.Owner));
    }

    [Fact]
    public async Task GetTables_AliasWithoutOwnTables_ReturnsParentTables()
    {
        var tables = await fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "tprocvariant");

        var layout = Assert.Single(tables, t => t.View == "DocumentListLayout" && t.Layer == "base");
        Assert.Equal("tproc", layout.Owner);
        Assert.Equal("tprocDocumentListLayout", layout.PhysicalTable);
        var backend = Assert.Single(tables, t => t.View == "ServerProcessSettings");
        Assert.Equal("tprocvariant", backend.Owner);
        Assert.Equal(1, backend.RowCount);
    }

    [Fact]
    public async Task GetTables_UnknownProcess_ThrowsProcessNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "nosuch"));
        Assert.Equal(SmErrorCodes.ProcessNotFound, ex.Code);
    }

    [Fact]
    public async Task GetTables_UnsafeName_ThrowsInvalidName()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetTablesAsync(SqlServerFixture.ClientName, "x;drop"));
        Assert.Equal(SmErrorCodes.InvalidName, ex.Code);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~ProcessAndTableTests"`
Expected: build FAIL — `GetProcessesAsync` / `GetTablesAsync` not defined.

- [ ] **Step 3: Write the procedures**

`<db5>\settingsmanagement\030-procedure-GetProcesses.txt`:

```sql
CREATE OR ALTER PROCEDURE sm.GetProcesses
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.NAME              AS name,
           p.DISPLAYLABEL      AS displaylabel,
           p.TAG               AS tag,
           p.PROCESSTYPE       AS processtype,
           p.DATAPROCEDURES    AS dataprocedures,
           p.SETTINGPROCEDURES AS settingprocedures,
           p.ENVIRONMENT       AS environment,
           p.ORDERNR           AS ordernr,
           (SELECT STRING_AGG(v.NAME, N',') WITHIN GROUP (ORDER BY v.NAME)
            FROM dbo.processtypes AS v
            WHERE v.SETTINGPROCEDURES = p.NAME AND v.NAME <> p.NAME) AS variants,
           (SELECT COUNT(*) FROM sm.ProcessTables(p.NAME)) AS tablecount
    FROM dbo.processtypes AS p
    ORDER BY p.ORDERNR, p.NAME;
END
GO
```

`<db5>\settingsmanagement\031-procedure-GetTables.txt`:

```sql
CREATE OR ALTER PROCEDURE sm.GetTables
    @process nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    IF sm.IsSafeName(@process) = 0
        THROW 51001, N'Invalid process name.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.processtypes WHERE NAME = @process)
        THROW 51003, N'Process not found.', 1;

    DECLARE @alias nvarchar(100) =
        (SELECT TOP (1) NULLIF(SETTINGPROCEDURES, NAME) FROM dbo.processtypes WHERE NAME = @process);

    WITH t AS
    (
        SELECT pt.physicaltable, pt.[view], pt.layer, @process AS owner
        FROM sm.ProcessTables(@process) AS pt
        UNION ALL
        SELECT pt.physicaltable, pt.[view], pt.layer, @alias
        FROM sm.ProcessTables(@alias) AS pt
        WHERE @alias IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM sm.ProcessTables(@process) AS own
                          WHERE own.[view] = pt.[view] AND own.layer = pt.layer)
    )
    SELECT t.[view],
           t.layer,
           t.physicaltable,
           t.owner,
           CAST(ISNULL((SELECT SUM(p.rows) FROM sys.partitions AS p
                        WHERE p.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.physicaltable)) AND p.index_id IN (0, 1)), 0) AS int) AS [rowcount],
           CASE
               WHEN t.layer <> N'base' THEN NULL
               WHEN OBJECT_ID(N'dbo.' + QUOTENAME(N'Get' + @process + t.[view] + N'Info'), N'P') IS NOT NULL THEN N'Get' + @process + t.[view] + N'Info'
               WHEN OBJECT_ID(N'dbo.' + QUOTENAME(N'Get' + @process + t.[view] + N'Info-'), N'P') IS NOT NULL THEN N'Get' + @process + t.[view] + N'Info-'
           END AS overrideprocedure,
           CAST(CASE
                    WHEN t.layer = N'base' AND OBJECT_ID(N'dbo.' + QUOTENAME(N'Get' + @process + t.[view] + N'Info'), N'P') IS NOT NULL THEN 1
                    ELSE 0
                END AS bit) AS overrideactive
    FROM t
    UNION ALL
    SELECT N'ServerProcessSettings', N'base', N'ServerProcessSettings', @process,
           (SELECT COUNT(*) FROM dbo.ServerProcessSettings WHERE LEFT(NAME, LEN(@process) + 1) = @process + N'.'),
           NULL, CAST(0 AS bit)
    ORDER BY 1, 2;
END
GO
```

- [ ] **Step 4: Add the `SmClient` methods**

Insert into `SmClient` (before `QueryAsync`):

```csharp
    public Task<IReadOnlyList<SmProcess>> GetProcessesAsync(string client, CancellationToken cancellationToken = default) =>
        QueryAsync<SmProcess>(client, "sm.GetProcesses", null, cancellationToken);

    public Task<IReadOnlyList<SmTable>> GetTablesAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmTable>(client, "sm.GetTables", new { process }, cancellationToken);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~ProcessAndTableTests"`
Expected: 5 passed.

- [ ] **Step 6: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: GetProcesses, GetTables"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient processes and tables"
```

---

### Task 4: `sm.GetSettings` (raw rows per layer)

**Files:**
- Create: `<db5>\settingsmanagement\040-procedure-GetSettings.txt`
- Modify: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Test: `<root>\VTMF.Settings.Tests\Sm\GetSettingsTests.cs`

**Interfaces:**
- Consumes: `sm.IsSafeName`, `sm.ResolveLayer`, `SmSettingRow`.
- Produces: `Task<IReadOnlyList<SmSettingRow>> GetSettingsAsync(string client, string process, string table, string layer = "base", string? namePrefix = null, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\GetSettingsTests.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class GetSettingsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task BackendTable_StripsPrefixExcludesWorkersAndMasksSecrets()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "ServerProcessSettings");

        Assert.Equal(
            [("Directo.ApiKey", "********"), ("Document.Query.Join", "True"), ("Document.State.Terminal.New", "NEW"), ("Unused.Setting", "1")],
            rows.Select(r => (r.Name, r.Value)).ToArray());
    }

    [Fact]
    public async Task BaseLayer_ReturnsAllRowsInOrderIncludingEmptyValues()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout");

        Assert.Equal(
            ["Column", "Column.DOCNR.Caption", "Column", "Column.DATE.Caption", "Grid.PageSize", "Grid.Empty", "Grid.Secret"],
            rows.Select(r => r.Name).ToArray());
        Assert.Equal("********", rows.Single(r => r.Name == "Grid.Secret").Value);
        Assert.All(rows, r => Assert.Equal("base", r.Layer));
    }

    [Theory]
    [InlineData("env:wterminal2", 4)]
    [InlineData("lang:en", 3)]
    public async Task OverlayLayers_ReadOverlayTablesWithLayerWorkercode(string layer, int expectedCount)
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout", layer);

        Assert.Equal(expectedCount, rows.Count);
        Assert.All(rows, r => Assert.Equal(layer, r.Layer));
    }

    [Fact]
    public async Task NamePrefix_FiltersRows()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout", namePrefix: "Column.");

        Assert.Equal(["Column.DOCNR.Caption", "Column.DATE.Caption"], rows.Select(r => r.Name).ToArray());
    }

    [Theory]
    [InlineData("tproc", "DocumentListLayout", "env:Bad;", SmErrorCodes.InvalidLayer)]
    [InlineData("tproc", "DocumentListLayout", "worker:bob", SmErrorCodes.InvalidLayer)]
    [InlineData("tproc", "DocumentListLayout", "env:WTERMINAL2", SmErrorCodes.InvalidLayer)]
    [InlineData("x;drop", "DocumentListLayout", "base", SmErrorCodes.InvalidName)]
    [InlineData("tproc", "Document ListLayout", "base", SmErrorCodes.InvalidName)]
    [InlineData("nosuch", "DocumentListLayout", "base", SmErrorCodes.ProcessNotFound)]
    [InlineData("tproc", "Nope", "base", SmErrorCodes.TableNotFound)]
    [InlineData("tproc", "ServerProcessSettings", "env:wterminal2", SmErrorCodes.LayerNotSupported)]
    public async Task InvalidInput_ThrowsCodedError(string process, string table, string layer, string expectedCode)
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetSettingsAsync(Client, process, table, layer));

        Assert.Equal(expectedCode, ex.Code);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~GetSettingsTests"`
Expected: build FAIL — `GetSettingsAsync` not defined.

- [ ] **Step 3: Write the procedure**

`<db5>\settingsmanagement\040-procedure-GetSettings.txt`:

```sql
CREATE OR ALTER PROCEDURE sm.GetSettings
    @process    nvarchar(100),
    @table      nvarchar(100),
    @layer      nvarchar(110) = N'base',
    @namePrefix nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF sm.IsSafeName(@process) = 0 OR sm.IsSafeName(@table) = 0
        THROW 51001, N'Invalid process or table name.', 1;

    DECLARE @kind nvarchar(10), @suffix nvarchar(100), @workercode nvarchar(100);
    SELECT @kind = kind, @suffix = suffix, @workercode = workercode FROM sm.ResolveLayer(@layer);
    IF @kind IS NULL
        THROW 51002, N'Invalid layer. Use base, env:{name} or lang:{code} (lowercase letters, digits, underscore).', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.processtypes WHERE NAME = @process)
        THROW 51003, N'Process not found.', 1;

    DECLARE @mask nvarchar(10) = N'********';

    IF @table = N'ServerProcessSettings'
    BEGIN
        IF @kind <> N'base'
            THROW 51005, N'ServerProcessSettings supports only the base layer.', 1;

        SELECT N'ServerProcessSettings'                                        AS [table],
               N'base'                                                         AS layer,
               SUBSTRING(s.NAME, LEN(@process) + 2, 200)                       AS name,
               CASE WHEN s.settingtype = N'ServerSetting' THEN @mask ELSE s.VALUE END AS value,
               s.settingtype,
               s.LISTVALUE                                                     AS listvalue,
               s.ordernr
        FROM dbo.ServerProcessSettings AS s
        WHERE LEFT(s.NAME, LEN(@process) + 1) = @process + N'.'
          AND s.workercode = N'ALL'
          AND (@namePrefix IS NULL OR LEFT(SUBSTRING(s.NAME, LEN(@process) + 2, 200), LEN(@namePrefix)) = @namePrefix)
        ORDER BY s.ordernr, s.NAME;
        RETURN;
    END

    DECLARE @physical sysname = @process + @table + CASE WHEN @kind = N'base' THEN N'' ELSE @suffix END;
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@physical), N'U') IS NULL
    BEGIN
        DECLARE @msg nvarchar(2048) = CONCAT(N'Table not found: ', @physical, N'. If the process uses SETTINGPROCEDURES, read the parent process.');
        THROW 51004, @msg, 1;
    END

    DECLARE @sql nvarchar(max) = N'
        SELECT @table AS [table], @layer AS layer, s.NAME AS name,
               CASE WHEN s.settingtype = N''ServerSetting'' THEN @mask ELSE s.VALUE END AS value,
               s.settingtype, s.LISTVALUE AS listvalue, s.ordernr
        FROM dbo.' + QUOTENAME(@physical) + N' AS s
        WHERE s.workercode = @workercode
          AND (@namePrefix IS NULL OR LEFT(s.NAME, LEN(@namePrefix)) = @namePrefix)
        ORDER BY s.ordernr, s.NAME;';

    EXEC sys.sp_executesql @sql,
        N'@table nvarchar(100), @layer nvarchar(110), @mask nvarchar(10), @workercode nvarchar(100), @namePrefix nvarchar(200)',
        @table, @layer, @mask, @workercode, @namePrefix;
END
GO
```

- [ ] **Step 4: Add the `SmClient` method**

```csharp
    public Task<IReadOnlyList<SmSettingRow>> GetSettingsAsync(
        string client, string process, string table, string layer = "base", string? namePrefix = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync<SmSettingRow>(client, "sm.GetSettings", new { process, table, layer, namePrefix }, cancellationToken);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~GetSettingsTests"`
Expected: 13 passed.

- [ ] **Step 6: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: GetSettings"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient raw settings per layer"
```

---

### Task 5: `sm.GetEffectiveSettings` (parity with WS)

Replicates `VTMF.Terminal.WS\Readers\LayoutListSettingReader.Read` including its quirks (documented in `docs/settings-architecture.md` §10): environment and language overrides match only base rows; language rows whose name exists only in the environment layer are appended as duplicates; setting-type comparisons are case-sensitive; empty base values are skipped. Backend (`ServerProcessSettings`) replicates `dbo.GetProcessSettingValues` for the `ALL` layer (process first, `SETTINGPROCEDURES` parent as fallback, empty values dropped).

**Files:**
- Create: `<db5>\settingsmanagement\050-procedure-GetEffectiveSettings.txt`
- Modify: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Test: `<root>\VTMF.Settings.Tests\Sm\EffectiveSettingsTests.cs`

**Interfaces:**
- Consumes: `SmEffectiveInfo`, `SmEffectiveRow`, `SmEffectiveSettings`, `SmException.TryTranslate`.
- Produces: `Task<SmEffectiveSettings> GetEffectiveSettingsAsync(string client, string process, string table, string? environment = null, string? language = null, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\EffectiveSettingsTests.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class EffectiveSettingsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task Layout_WithEnvironmentAndLanguage_MatchesWsReader()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "wterminal2", "en");

        Assert.Equal("tprocDocumentListLayout", result.Info.PhysicalTable);
        Assert.False(result.Info.AliasUsed);
        Assert.Null(result.Info.OverrideProcedure);
        Assert.Equal(
            [
                ("Column", "DOCNR", "base"),
                ("Column.DOCNR.Caption", "Doc no (env)", "env"),
                ("Column", "DATE", "base"),
                ("Column.DATE.Caption", "Date", "lang"),
                ("Grid.PageSize", "20", "base"),
                ("Grid.Secret", "********", "base"),
                ("Column", "STATUS", "env"),
                ("Grid.ShowFilterRow", "true", "env"),
                ("Grid.ShowFilterRow", "false", "lang"),
                ("Lang.Only", "x", "lang"),
            ],
            result.Rows.Select(r => (r.Name, r.Value, r.Source)).ToArray());
    }

    [Fact]
    public async Task Layout_DefaultEnvironmentAndNoLanguage_ReturnsBaseOnly()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "wterminal", null);

        Assert.Equal(6, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal("base", r.Source));
        Assert.Equal("Dok nr", result.Rows.Single(r => r.Name == "Column.DOCNR.Caption").Value);
    }

    [Fact]
    public async Task Layout_AliasProcess_UsesParentTable()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tprocvariant", "DocumentListLayout");

        Assert.True(result.Info.AliasUsed);
        Assert.Equal("tprocDocumentListLayout", result.Info.PhysicalTable);
        Assert.Equal(6, result.Rows.Count);
    }

    [Fact]
    public async Task Layout_WithActiveInfoProcedure_ReportsOverride()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentItemsListLayout");

        Assert.Equal("GettprocDocumentItemsListLayoutInfo", result.Info.OverrideProcedure);
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task Backend_AliasProcess_FallsBackToParentAndMasksSecrets()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tprocvariant", "ServerProcessSettings");

        Assert.Equal(
            [
                ("Directo.ApiKey", "********", "parent"),
                ("Document.Query.Join", "True", "parent"),
                ("Document.State.Terminal.New", "VNEW", "process"),
                ("Unused.Setting", "1", "parent"),
            ],
            result.Rows.Select(r => (r.Name, r.Value, r.Source)).ToArray());
    }

    [Fact]
    public async Task UnsafeEnvironment_ThrowsInvalidLayer()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "x;drop"));

        Assert.Equal(SmErrorCodes.InvalidLayer, ex.Code);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~EffectiveSettingsTests"`
Expected: build FAIL — `GetEffectiveSettingsAsync` not defined.

- [ ] **Step 3: Write the procedure**

`<db5>\settingsmanagement\050-procedure-GetEffectiveSettings.txt`:

```sql
-- Result set 1: resolution info. Result set 2: effective rows with the layer that supplied each value.
CREATE OR ALTER PROCEDURE sm.GetEffectiveSettings
    @process     nvarchar(100),
    @table       nvarchar(100),
    @environment nvarchar(100) = NULL,
    @language    nvarchar(10)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF sm.IsSafeName(@process) = 0 OR sm.IsSafeName(@table) = 0
        THROW 51001, N'Invalid process or table name.', 1;
    IF (ISNULL(@environment, N'') <> N'' AND sm.IsSafeName(@environment) = 0)
       OR (ISNULL(@language, N'') <> N'' AND sm.IsSafeName(@language) = 0)
        THROW 51002, N'Invalid environment or language.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.processtypes WHERE NAME = @process)
        THROW 51003, N'Process not found.', 1;

    DECLARE @mask nvarchar(10) = N'********';
    DECLARE @alias nvarchar(100) = (SELECT TOP (1) COALESCE(SETTINGPROCEDURES, NAME) FROM dbo.processtypes WHERE NAME = @process);

    IF @table = N'ServerProcessSettings'
    BEGIN
        SELECT N'ServerProcessSettings'                                   AS physicaltable,
               CAST(CASE WHEN @alias <> @process THEN 1 ELSE 0 END AS bit) AS aliasused,
               CAST(NULL AS nvarchar(200))                                AS overrideprocedure,
               CAST(NULL AS nvarchar(200))                                AS additionalprocedure;

        WITH c AS
        (
            SELECT SUBSTRING(s.NAME, LEN(@process) + 2, 200) AS name, s.VALUE, s.settingtype, s.LISTVALUE, s.ordernr,
                   N'process' AS source, 1 AS prio
            FROM dbo.ServerProcessSettings AS s
            WHERE LEFT(s.NAME, LEN(@process) + 1) = @process + N'.' AND s.workercode = N'ALL'
            UNION ALL
            SELECT SUBSTRING(s.NAME, LEN(@alias) + 2, 200), s.VALUE, s.settingtype, s.LISTVALUE, s.ordernr,
                   N'parent', 2
            FROM dbo.ServerProcessSettings AS s
            WHERE @alias <> @process AND LEFT(s.NAME, LEN(@alias) + 1) = @alias + N'.' AND s.workercode = N'ALL'
        ),
        r AS
        (
            SELECT *, ROW_NUMBER() OVER (PARTITION BY name ORDER BY prio) AS rn FROM c WHERE VALUE <> N''
        )
        SELECT name,
               CASE WHEN settingtype = N'ServerSetting' THEN @mask ELSE VALUE END AS value,
               settingtype, LISTVALUE AS listvalue, ordernr, source
        FROM r
        WHERE rn = 1
        ORDER BY name;
        RETURN;
    END

    DECLARE @infoproc sysname = N'Get' + @process + @table + N'Info';
    DECLARE @override sysname = CASE WHEN OBJECT_ID(N'dbo.' + QUOTENAME(@infoproc), N'P') IS NOT NULL THEN @infoproc END;
    DECLARE @base sysname =
        CASE
            WHEN OBJECT_ID(N'dbo.' + QUOTENAME(@process + @table), N'U') IS NOT NULL THEN @process + @table
            WHEN OBJECT_ID(N'dbo.' + QUOTENAME(@alias + @table), N'U') IS NOT NULL THEN @alias + @table
        END;
    DECLARE @additional sysname =
        CASE WHEN @base IS NOT NULL AND OBJECT_ID(N'dbo.' + QUOTENAME(N'Get' + @base + N'Additional'), N'P') IS NOT NULL
             THEN N'Get' + @base + N'Additional' END;

    SELECT @base                                                                         AS physicaltable,
           CAST(CASE WHEN @base IS NOT NULL AND @base <> @process + @table THEN 1 ELSE 0 END AS bit) AS aliasused,
           @override                                                                     AS overrideprocedure,
           @additional                                                                   AS additionalprocedure;

    CREATE TABLE #rows
    (
        seq         int IDENTITY(1, 1) PRIMARY KEY,
        name        nvarchar(200)  NOT NULL,
        value       nvarchar(400)  NOT NULL,
        settingtype nvarchar(20)   NOT NULL,
        listvalue   nvarchar(1600) NULL,
        ordernr     int            NOT NULL,
        origin      nvarchar(10)   NOT NULL,
        source      nvarchar(10)   NOT NULL
    );
    CREATE TABLE #layer
    (
        name        nvarchar(200)  NOT NULL,
        value       nvarchar(400)  NOT NULL,
        settingtype nvarchar(20)   NOT NULL,
        listvalue   nvarchar(1600) NULL,
        ordernr     int            NOT NULL
    );

    IF @base IS NOT NULL
    BEGIN
        DECLARE @sql nvarchar(max);

        -- Step 1: base rows (workercode ALL, non-empty VALUE), ordered by ordernr
        SET @sql = N'INSERT INTO #layer (name, value, settingtype, listvalue, ordernr)
                     SELECT ISNULL(NAME, N''Grid''), ISNULL(VALUE, N''''), ISNULL(settingtype, N''Setting''), LISTVALUE, ISNULL(ordernr, 0)
                     FROM dbo.' + QUOTENAME(@base) + N' WHERE workercode = N''ALL''';
        EXEC sys.sp_executesql @sql;
        INSERT INTO #rows (name, value, settingtype, listvalue, ordernr, origin, source)
        SELECT name, value, settingtype, listvalue, ordernr, N'base', N'base'
        FROM #layer
        WHERE value <> N''
        ORDER BY ordernr;

        -- Step 2: environment overlay {base}{environment}, rows with workercode = environment
        IF ISNULL(@environment, N'') NOT IN (N'', N'wterminal')
           AND OBJECT_ID(N'dbo.' + QUOTENAME(@base + @environment), N'U') IS NOT NULL
        BEGIN
            TRUNCATE TABLE #layer;
            SET @sql = N'INSERT INTO #layer (name, value, settingtype, listvalue, ordernr)
                         SELECT ISNULL(NAME, N''Grid''), ISNULL(VALUE, N''''), ISNULL(settingtype, N''Setting''), LISTVALUE, ISNULL(ordernr, 0)
                         FROM dbo.' + QUOTENAME(@base + @environment) + N' WHERE workercode = @env';
            EXEC sys.sp_executesql @sql, N'@env nvarchar(100)', @environment;

            -- 2a: override matching base Setting rows
            UPDATE r
               SET value = l.value, listvalue = l.listvalue, source = N'env'
            FROM #rows AS r
            JOIN #layer AS l ON l.name = r.name
            WHERE r.origin = N'base'
              AND r.settingtype COLLATE Latin1_General_BIN = N'Setting'
              AND l.settingtype COLLATE Latin1_General_BIN = N'Setting';

            -- 2b: append SettingGroup rows not present in base (NAME+VALUE concatenated, as WS does)
            INSERT INTO #rows (name, value, settingtype, listvalue, ordernr, origin, source)
            SELECT l.name, l.value, l.settingtype, l.listvalue, l.ordernr, N'env', N'env'
            FROM #layer AS l
            WHERE l.settingtype COLLATE Latin1_General_BIN = N'SettingGroup'
              AND NOT EXISTS (SELECT 1 FROM #rows AS r
                              WHERE r.origin = N'base'
                                AND r.settingtype COLLATE Latin1_General_BIN = N'SettingGroup'
                                AND r.name + r.value = l.name + l.value)
            ORDER BY l.ordernr;

            -- 2c: append Setting rows not present in base
            INSERT INTO #rows (name, value, settingtype, listvalue, ordernr, origin, source)
            SELECT l.name, l.value, l.settingtype, l.listvalue, l.ordernr, N'env', N'env'
            FROM #layer AS l
            WHERE l.settingtype COLLATE Latin1_General_BIN = N'Setting'
              AND NOT EXISTS (SELECT 1 FROM #rows AS r
                              WHERE r.origin = N'base'
                                AND r.settingtype COLLATE Latin1_General_BIN = N'Setting'
                                AND r.name = l.name)
            ORDER BY l.ordernr;
        END

        -- Step 3: language overlay {base}{language}, rows with workercode ALL; matches base rows only
        IF ISNULL(@language, N'') <> N''
           AND OBJECT_ID(N'dbo.' + QUOTENAME(@base + @language), N'U') IS NOT NULL
        BEGIN
            TRUNCATE TABLE #layer;
            SET @sql = N'INSERT INTO #layer (name, value, settingtype, listvalue, ordernr)
                         SELECT ISNULL(NAME, N''Grid''), ISNULL(VALUE, N''''), ISNULL(settingtype, N''Setting''), LISTVALUE, ISNULL(ordernr, 0)
                         FROM dbo.' + QUOTENAME(@base + @language) + N' WHERE workercode = N''ALL''';
            EXEC sys.sp_executesql @sql;

            UPDATE r
               SET value = l.value, listvalue = l.listvalue, source = N'lang'
            FROM #rows AS r
            JOIN #layer AS l ON l.name = r.name
            WHERE r.origin = N'base'
              AND r.settingtype COLLATE Latin1_General_BIN = N'Setting'
              AND l.settingtype COLLATE Latin1_General_BIN = N'Setting';

            INSERT INTO #rows (name, value, settingtype, listvalue, ordernr, origin, source)
            SELECT l.name, l.value, l.settingtype, l.listvalue, l.ordernr, N'lang', N'lang'
            FROM #layer AS l
            WHERE l.settingtype COLLATE Latin1_General_BIN = N'Setting'
              AND NOT EXISTS (SELECT 1 FROM #rows AS r
                              WHERE r.origin = N'base'
                                AND r.settingtype COLLATE Latin1_General_BIN = N'Setting'
                                AND r.name = l.name)
            ORDER BY l.ordernr;
        END
    END

    SELECT name,
           CASE WHEN settingtype = N'ServerSetting' THEN @mask ELSE value END AS value,
           settingtype, listvalue, ordernr, source
    FROM #rows
    ORDER BY seq;
END
GO
```

- [ ] **Step 4: Add the `SmClient` method**

Add `using System.Data;` is already present. Insert:

```csharp
    public async Task<SmEffectiveSettings> GetEffectiveSettingsAsync(
        string client, string process, string table, string? environment = null, string? language = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connections.GetConnection(client);
        try
        {
            var command = new CommandDefinition("sm.GetEffectiveSettings", new { process, table, environment, language },
                commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
            using var grid = await connection.QueryMultipleAsync(command).ConfigureAwait(false);
            var info = await grid.ReadSingleAsync<SmEffectiveInfo>().ConfigureAwait(false);
            var rows = (await grid.ReadAsync<SmEffectiveRow>().ConfigureAwait(false)).AsList();
            return new SmEffectiveSettings { Info = info, Rows = rows };
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~EffectiveSettingsTests"`
Expected: 6 passed.

- [ ] **Step 6: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: GetEffectiveSettings (WS reader parity)"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient effective settings"
```

---

### Task 6: `sm.GetSettingsUsage` (backend catalogue)

Scans `dbo` module source for backend setting reads. Uses only `REGEXP_SUBSTR` / `REGEXP_COUNT` and a temp tally table so it runs at compatibility level 150 (Review Focus 1). Patterns are RE2 syntax with flag `i`.

**Files:**
- Create: `<db5>\settingsmanagement\013-function-UnquoteSqlLiteral.txt`
- Create: `<db5>\settingsmanagement\060-procedure-GetSettingsUsage.txt`
- Modify: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Test: `<root>\VTMF.Settings.Tests\Sm\SettingsUsageTests.cs`

**Interfaces:**
- Consumes: `sm.IsSafeName`, `SmSettingUsage`.
- Produces: `sm.UnquoteSqlLiteral(@s nvarchar(1600)) RETURNS nvarchar(1600)`; `Task<IReadOnlyList<SmSettingUsage>> GetSettingsUsageAsync(string client, string process, string? namePrefix = null, CancellationToken cancellationToken = default)`.
- Result columns: `settingname, procedurename, callstyle ('psv'|'legacy'|'direct'|'unparsed'|'none'), attribution ('literal'|'procname'|NULL), defaultincode, configuredvalue, configuredin ('process'|'parent'|NULL), workeroverrides, unused, detail`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\SettingsUsageTests.cs`:

```csharp
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsUsageTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task Usage_ReportsPsvLegacyDirectUnparsedAndUnused()
    {
        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tproc");

        Assert.Equal(
            [
                ("Directo.ApiKey", null, "none", null, "********", true),
                ("Document.Query.Join", "GetDocumentsFortproc", "psv", "False", "True", false),
                ("Document.RegDocState.RemoveNoQty", "RegDocStateFortprocFinished", "legacy", "False", null, false),
                ("Document.State.Terminal.New", "GetDocumentsFortproc", "psv", "UUS", "NEW", false),
                ("Unused.Setting", null, "none", null, "1", true),
                (null, "GetDocumentsFortproc", "unparsed", null, null, false),
                (null, "RegDocStateFortprocFinished", "direct", null, null, false),
            ],
            usage.Select(u => (u.SettingName, u.ProcedureName, u.CallStyle, u.DefaultInCode, u.ConfiguredValue, u.Unused)).ToArray());
        Assert.Equal(1, usage.Single(u => u.SettingName == "Document.Query.Join").WorkerOverrides);
        Assert.Equal("1", usage.Single(u => u.CallStyle == "unparsed").Detail);
        Assert.Equal("ServerProcessSettings", usage.Single(u => u.CallStyle == "direct").Detail);
        Assert.DoesNotContain(usage, u => u.ProcedureName == "GetDocumentsFortother");
    }

    [Fact]
    public async Task Usage_AliasProcess_IncludesParentReadsAndResolvesConfiguredValues()
    {
        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tprocvariant", "Document.");

        var newState = usage.Single(u => u.SettingName == "Document.State.Terminal.New");
        Assert.Equal(("VNEW", "process"), (newState.ConfiguredValue, newState.ConfiguredIn));
        var join = usage.Single(u => u.SettingName == "Document.Query.Join");
        Assert.Equal(("True", "parent", 0), (join.ConfiguredValue, join.ConfiguredIn, join.WorkerOverrides));
        Assert.All(usage, u => Assert.StartsWith("Document.", u.SettingName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_RunsAtCompatibilityLevel150()
    {
        await fixture.ExecuteOnTestAsync("IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) <> 150 THROW 50000, 'fixture must run at level 150', 1;");

        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tproc");

        Assert.NotEmpty(usage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~SettingsUsageTests"`
Expected: build FAIL — `GetSettingsUsageAsync` not defined.

- [ ] **Step 3: Write the SQL**

`<db5>\settingsmanagement\013-function-UnquoteSqlLiteral.txt`:

```sql
-- 'abc' / N'abc' -> abc (with '' -> '); anything else (variables, numbers) is returned trimmed.
CREATE OR ALTER FUNCTION sm.UnquoteSqlLiteral(@s nvarchar(1600))
RETURNS nvarchar(1600)
AS
BEGIN
    DECLARE @t nvarchar(1600) = LTRIM(RTRIM(@s));
    IF @t LIKE N'N''%'''
        SET @t = SUBSTRING(@t, 2, 1600);
    IF LEN(@t) >= 2 AND LEFT(@t, 1) = N'''' AND RIGHT(@t, 1) = N''''
        RETURN REPLACE(SUBSTRING(@t, 2, LEN(@t) - 2), N'''''', N'''');
    RETURN @t;
END
GO
```

`<db5>\settingsmanagement\060-procedure-GetSettingsUsage.txt`:

```sql
CREATE OR ALTER PROCEDURE sm.GetSettingsUsage
    @process    nvarchar(100),
    @namePrefix nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF sm.IsSafeName(@process) = 0
        THROW 51001, N'Invalid process name.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.processtypes WHERE NAME = @process)
        THROW 51003, N'Process not found.', 1;

    DECLARE @mask nvarchar(10) = N'********';
    DECLARE @alias nvarchar(100) = (SELECT TOP (1) NULLIF(SETTINGPROCEDURES, NAME) FROM dbo.processtypes WHERE NAME = @process);

    -- group 1 = setting name, group 2 = default expression
    DECLARE @psvPattern nvarchar(400) =
        N'GetPSV[IB]?\s*\(\s*@\w+\s*,\s*N?''([^'']+)''\s*,\s*(N?''(?:[^'']|'''')*''|[^)]*)\)';
    -- group 1 = process argument, group 2 = setting name, group 3 = default expression
    DECLARE @legacyPattern nvarchar(400) =
        N'GetProcessSettingValue(?:Int|Dec)?\s*\(\s*(N?''\w+''|@\w+)\s*,\s*N?''([^'']+)''\s*,\s*[^,]+,\s*(N?''(?:[^'']|'''')*''|[^)]*)\)';
    DECLARE @psvCallPattern nvarchar(100) = N'GetPSV[IB]?\s*\(';
    DECLARE @legacyCallPattern nvarchar(100) = N'GetProcessSettingValue(?:Int|Dec)?\s*\(';
    DECLARE @directPattern nvarchar(200) = N'from\s+(?:\[?dbo\]?\.)?\[?ServerProcessSettings\]?';

    CREATE TABLE #procs (name nvarchar(100) NOT NULL PRIMARY KEY);
    INSERT INTO #procs (name) VALUES (@process);
    IF @alias IS NOT NULL
        INSERT INTO #procs (name) VALUES (@alias);

    CREATE TABLE #tally (n int NOT NULL PRIMARY KEY);
    INSERT INTO #tally (n)
    SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
    FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b;

    -- Candidate modules and their attribution to the requested process (or its parent)
    CREATE TABLE #mods
    (
        procname       sysname       NOT NULL PRIMARY KEY,
        definition     nvarchar(max) NOT NULL,
        literalprocess nvarchar(100) NULL,
        attribution    nvarchar(10)  NULL
    );
    INSERT INTO #mods (procname, definition, literalprocess)
    SELECT o.name, m.definition,
           REGEXP_SUBSTR(m.definition, N'GetProcessSettingValues\s*\(\s*N?''(\w+)''', 1, 1, 'i', 1)
    FROM sys.sql_modules AS m
    JOIN sys.objects AS o ON o.object_id = m.object_id
    WHERE o.schema_id = SCHEMA_ID(N'dbo')
      AND o.name NOT IN (N'GetPSV', N'GetPSVI', N'GetPSVB', N'GetProcessSettingValue', N'GetProcessSettingValueInt',
                         N'GetProcessSettingValueDec', N'GetProcessSettingValues', N'ShowAllServerSettings')
      AND (m.definition LIKE N'%GetPSV%' OR m.definition LIKE N'%GetProcessSettingValue%' OR m.definition LIKE N'%ServerProcessSettings%');

    UPDATE md
       SET attribution = CASE
                             WHEN md.literalprocess IN (SELECT name FROM #procs) THEN N'literal'
                             WHEN md.literalprocess IS NULL
                                  AND EXISTS (SELECT 1 FROM #procs AS p WHERE md.procname LIKE N'%' + p.name + N'%') THEN N'procname'
                         END
    FROM #mods AS md;

    CREATE TABLE #usage
    (
        settingname   nvarchar(200)  NULL,
        procedurename sysname        NULL,
        callstyle     nvarchar(10)   NOT NULL,
        attribution   nvarchar(10)   NULL,
        defaultincode nvarchar(1600) NULL,
        detail        nvarchar(400)  NULL
    );

    -- @psv reads (recommended pattern)
    INSERT INTO #usage (settingname, procedurename, callstyle, attribution, defaultincode)
    SELECT REGEXP_SUBSTR(md.definition, @psvPattern, 1, t.n, 'i', 1),
           md.procname, N'psv', md.attribution,
           REGEXP_SUBSTR(md.definition, @psvPattern, 1, t.n, 'i', 2)
    FROM #mods AS md
    JOIN #tally AS t ON t.n <= REGEXP_COUNT(md.definition, @psvPattern, 1, 'i')
    WHERE md.attribution IS NOT NULL;

    -- Legacy per-setting reads; attribution comes from the call's own process argument when it is a literal
    INSERT INTO #usage (settingname, procedurename, callstyle, attribution, defaultincode)
    SELECT y.settingname, y.procname, N'legacy',
           CASE
               WHEN y.literalprocess IN (SELECT name FROM #procs) THEN N'literal'
               WHEN y.literalprocess IS NULL
                    AND EXISTS (SELECT 1 FROM #procs AS p WHERE y.procname LIKE N'%' + p.name + N'%') THEN N'procname'
           END,
           y.defaultincode
    FROM (
        SELECT md.procname,
               CASE WHEN LEFT(x.processarg, 1) = N'@' THEN NULL ELSE sm.UnquoteSqlLiteral(x.processarg) END AS literalprocess,
               x.settingname,
               x.defaultincode
        FROM #mods AS md
        JOIN #tally AS t ON t.n <= REGEXP_COUNT(md.definition, @legacyPattern, 1, 'i')
        CROSS APPLY (SELECT REGEXP_SUBSTR(md.definition, @legacyPattern, 1, t.n, 'i', 1) AS processarg,
                            REGEXP_SUBSTR(md.definition, @legacyPattern, 1, t.n, 'i', 2) AS settingname,
                            REGEXP_SUBSTR(md.definition, @legacyPattern, 1, t.n, 'i', 3) AS defaultincode) AS x
    ) AS y
    WHERE y.literalprocess IN (SELECT name FROM #procs)
       OR (y.literalprocess IS NULL AND EXISTS (SELECT 1 FROM #procs AS p WHERE y.procname LIKE N'%' + p.name + N'%'));

    -- Calls the patterns could not parse (e.g. setting name passed as a variable)
    INSERT INTO #usage (settingname, procedurename, callstyle, attribution, detail)
    SELECT NULL, md.procname, N'unparsed', md.attribution, CAST(c.unparsed AS nvarchar(10))
    FROM #mods AS md
    CROSS APPLY (SELECT REGEXP_COUNT(md.definition, @psvCallPattern, 1, 'i')
                      + REGEXP_COUNT(md.definition, @legacyCallPattern, 1, 'i')
                      - REGEXP_COUNT(md.definition, @psvPattern, 1, 'i')
                      - REGEXP_COUNT(md.definition, @legacyPattern, 1, 'i') AS unparsed) AS c
    WHERE md.attribution IS NOT NULL AND c.unparsed > 0;

    -- Direct reads of ServerProcessSettings (anti-pattern)
    INSERT INTO #usage (settingname, procedurename, callstyle, attribution, detail)
    SELECT NULL, md.procname, N'direct', md.attribution, N'ServerProcessSettings'
    FROM #mods AS md
    WHERE md.attribution IS NOT NULL
      AND REGEXP_COUNT(md.definition, @directPattern, 1, 'i') > 0;

    WITH cfg AS
    (
        SELECT SUBSTRING(s.NAME, LEN(p.name) + 2, 200) AS settingname,
               CASE WHEN s.settingtype = N'ServerSetting' THEN @mask ELSE s.VALUE END AS value,
               CASE WHEN p.name = @process THEN N'process' ELSE N'parent' END AS configuredin,
               ROW_NUMBER() OVER (PARTITION BY SUBSTRING(s.NAME, LEN(p.name) + 2, 200)
                                  ORDER BY CASE WHEN p.name = @process THEN 1 ELSE 2 END) AS rn
        FROM dbo.ServerProcessSettings AS s
        JOIN #procs AS p ON LEFT(s.NAME, LEN(p.name) + 1) = p.name + N'.'
        WHERE s.workercode = N'ALL' AND s.VALUE <> N''
    ),
    workers AS
    (
        SELECT SUBSTRING(s.NAME, LEN(@process) + 2, 200) AS settingname, COUNT(*) AS cnt
        FROM dbo.ServerProcessSettings AS s
        WHERE LEFT(s.NAME, LEN(@process) + 1) = @process + N'.' AND s.workercode <> N'ALL'
        GROUP BY SUBSTRING(s.NAME, LEN(@process) + 2, 200)
    ),
    allrows AS
    (
        SELECT u.settingname, u.procedurename, u.callstyle, u.attribution,
               sm.UnquoteSqlLiteral(u.defaultincode) AS defaultincode, u.detail, CAST(0 AS bit) AS unused
        FROM #usage AS u
        UNION ALL
        SELECT c.settingname, NULL, N'none', NULL, NULL, NULL, CAST(1 AS bit)
        FROM cfg AS c
        WHERE c.rn = 1 AND c.configuredin = N'process'
          AND NOT EXISTS (SELECT 1 FROM #usage AS u WHERE u.settingname = c.settingname)
    )
    SELECT a.settingname,
           a.procedurename,
           a.callstyle,
           a.attribution,
           a.defaultincode,
           c.value           AS configuredvalue,
           c.configuredin,
           ISNULL(w.cnt, 0)  AS workeroverrides,
           a.unused,
           a.detail
    FROM allrows AS a
    LEFT JOIN cfg AS c ON c.settingname = a.settingname AND c.rn = 1
    LEFT JOIN workers AS w ON w.settingname = a.settingname
    WHERE @namePrefix IS NULL OR LEFT(a.settingname, LEN(@namePrefix)) = @namePrefix
    ORDER BY CASE WHEN a.settingname IS NULL THEN 1 ELSE 0 END, a.settingname, a.procedurename, a.callstyle;
END
GO
```

If `REGEXP_SUBSTR`/`REGEXP_COUNT` raise an error at compatibility level 150, **stop and report** — that invalidates spec decision D8 for phase 1 and needs a design decision (do not switch to C# parsing on your own).

- [ ] **Step 4: Add the `SmClient` method**

```csharp
    public Task<IReadOnlyList<SmSettingUsage>> GetSettingsUsageAsync(
        string client, string process, string? namePrefix = null, CancellationToken cancellationToken = default) =>
        QueryAsync<SmSettingUsage>(client, "sm.GetSettingsUsage", new { process, namePrefix }, cancellationToken);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~SettingsUsageTests"`
Expected: 3 passed.

- [ ] **Step 6: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: GetSettingsUsage backend catalogue"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient settings usage catalogue"
```

---

### Task 7: `sm.GetLegacyEnvRows`

**Files:**
- Create: `<db5>\settingsmanagement\070-procedure-GetLegacyEnvRows.txt`
- Modify: `<root>\VTMF.Settings.Core\Sm\SmClient.cs`
- Test: `<root>\VTMF.Settings.Tests\Sm\LegacyEnvRowsTests.cs`

**Interfaces:**
- Consumes: `sm.ProcessTables`, `SmLegacyEnvRow`.
- Produces: `Task<IReadOnlyList<SmLegacyEnvRow>> GetLegacyEnvRowsAsync(string client, string process, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

`<root>\VTMF.Settings.Tests\Sm\LegacyEnvRowsTests.cs`:

```csharp
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class LegacyEnvRowsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task LegacyEnvRows_FindsEnvironmentRowsInBaseTables()
    {
        var rows = await fixture.Sm.GetLegacyEnvRowsAsync(SqlServerFixture.ClientName, "tproc");

        var row = Assert.Single(rows);
        Assert.Equal(
            ("tprocDocumentListLayout", "DocumentListLayout", "wterminal2", "Column.DOCNR.Caption", "Legacy", true),
            (row.PhysicalTable, row.View, row.Environment, row.Name, row.Value, row.OverlayExists));
    }

    [Fact]
    public async Task LegacyEnvRows_MasksServerSettings()
    {
        await fixture.ExecuteOnTestAsync(
            "INSERT INTO dbo.totherProcessSettings VALUES (N'Api.Key', N'plain-secret', N'ServerSetting', NULL, N'wterminal3', 20);");

        var rows = await fixture.Sm.GetLegacyEnvRowsAsync(SqlServerFixture.ClientName, "tother");

        var row = Assert.Single(rows);
        Assert.Equal(("********", false), (row.Value, row.OverlayExists));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~LegacyEnvRowsTests"`
Expected: build FAIL — `GetLegacyEnvRowsAsync` not defined.

- [ ] **Step 3: Write the procedure**

`<db5>\settingsmanagement\070-procedure-GetLegacyEnvRows.txt`:

```sql
-- Rows in base frontend tables whose workercode is not ALL: legacy environment overrides that belong
-- in the overlay table {table}{environment}.
CREATE OR ALTER PROCEDURE sm.GetLegacyEnvRows
    @process nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    IF sm.IsSafeName(@process) = 0
        THROW 51001, N'Invalid process name.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.processtypes WHERE NAME = @process)
        THROW 51003, N'Process not found.', 1;

    DECLARE @mask nvarchar(10) = N'********';

    CREATE TABLE #legacy
    (
        physicaltable sysname        NOT NULL,
        [view]        nvarchar(100)  NOT NULL,
        workercode    nvarchar(100)  NOT NULL,
        name          nvarchar(200)  NOT NULL,
        value         nvarchar(400)  NOT NULL,
        settingtype   nvarchar(20)   NOT NULL,
        listvalue     nvarchar(1600) NULL,
        ordernr       int            NOT NULL
    );

    DECLARE @t sysname, @v nvarchar(100), @sql nvarchar(max);
    DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
        SELECT physicaltable, [view] FROM sm.ProcessTables(@process) WHERE layer = N'base';
    OPEN tables;
    FETCH NEXT FROM tables INTO @t, @v;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'INSERT INTO #legacy (physicaltable, [view], workercode, name, value, settingtype, listvalue, ordernr)
                     SELECT @t, @v, workercode, NAME,
                            CASE WHEN settingtype = N''ServerSetting'' THEN @mask ELSE VALUE END,
                            settingtype, LISTVALUE, ordernr
                     FROM dbo.' + QUOTENAME(@t) + N' WHERE workercode <> N''ALL''';
        EXEC sys.sp_executesql @sql, N'@t sysname, @v nvarchar(100), @mask nvarchar(10)', @t, @v, @mask;
        FETCH NEXT FROM tables INTO @t, @v;
    END
    CLOSE tables;
    DEALLOCATE tables;

    SELECT l.physicaltable,
           l.[view],
           l.workercode AS environment,
           l.name,
           l.value,
           l.settingtype,
           l.listvalue,
           l.ordernr,
           CAST(CASE WHEN OBJECT_ID(N'dbo.' + QUOTENAME(l.physicaltable + l.workercode), N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS overlayexists
    FROM #legacy AS l
    ORDER BY l.physicaltable, l.workercode, l.ordernr;
END
GO
```

- [ ] **Step 4: Add the `SmClient` method**

```csharp
    public Task<IReadOnlyList<SmLegacyEnvRow>> GetLegacyEnvRowsAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmLegacyEnvRow>(client, "sm.GetLegacyEnvRows", new { process }, cancellationToken);
```

- [ ] **Step 5: Run the whole test suite**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj`
Expected: all tests pass (Tasks 1–7).

- [ ] **Step 6: Commit (two repos)**

```powershell
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 add settingsmanagement
git -C C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5 commit -m "settingsmanagement: GetLegacyEnvRows"
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Core VTMF.Settings.Tests
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(core): SmClient legacy environment rows"
```

---

### Task 8: MCP server with read tools

**Files:**
- Create: `<root>\VTMF.Settings.Mcp\VTMF.Settings.Mcp.csproj`
- Create: `<root>\VTMF.Settings.Mcp\Program.cs`
- Create: `<root>\VTMF.Settings.Mcp\McpTransportOption.cs`
- Create: `<root>\VTMF.Settings.Mcp\ToolResult.cs`
- Create: `<root>\VTMF.Settings.Mcp\Tools\SettingsReadTools.cs`
- Create: `<root>\VTMF.Settings.Mcp\appsettings.json`
- Modify: `<root>\eng\verify.config.json` (add the MCP project to `targets`)
- Test: `<root>\VTMF.Settings.Tests\Mcp\McpTransportOptionTests.cs`
- Test: `<root>\VTMF.Settings.Tests\Mcp\SettingsReadToolsTests.cs`

**Interfaces:**
- Consumes: `SmClient` (all phase-1 methods), `SqlConnectionProvider.GetAvailableClients()`, `AddSqlConnectionParameters()`, `AddSmClient()`.
- Produces: tools `list_clients`, `module_status`, `list_processes`, `list_tables`, `get_settings`, `get_effective_settings`, `settings_usage`, `legacy_env_rows`; `ToolResult<T>(bool Ok, T? Data, ToolError? Error)`; `McpTransportOption.Parse(string[] args) → McpTransport { Stdio, Http }`.

- [ ] **Step 1: Create the project**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
New-Item -ItemType Directory -Force VTMF.Settings.Mcp\Tools | Out-Null
```

`<root>\VTMF.Settings.Mcp\VTMF.Settings.Mcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisMode>Recommended</AnalysisMode>
    <AnalysisModePerformance>All</AnalysisModePerformance>
    <AnalysisModeSecurity>All</AnalysisModeSecurity>
    <!-- Same id as VTMF.Settings so developer secrets (client connections) are shared. -->
    <UserSecretsId>3ff0df02-19b1-4d65-9f9d-f8bed69b7342</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\VTMF.Settings.Core\VTMF.Settings.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="VTMF.Settings.Tests" />
  </ItemGroup>

</Project>
```

```powershell
dotnet add VTMF.Settings.Mcp package ModelContextProtocol
dotnet add VTMF.Settings.Mcp package ModelContextProtocol.AspNetCore
dotnet add VTMF.Settings.Mcp package Microsoft.Extensions.Configuration.UserSecrets
dotnet sln VTMF.Settings\VTMF.Settings.sln add VTMF.Settings.Mcp\VTMF.Settings.Mcp.csproj
dotnet add VTMF.Settings.Tests reference VTMF.Settings.Mcp\VTMF.Settings.Mcp.csproj
```

If `dotnet add package ModelContextProtocol` reports no stable version, rerun both MCP package commands with `--prerelease`.

Append `"VTMF.Settings.Mcp/VTMF.Settings.Mcp.csproj"` as the last entry of `targets` in `<root>\eng\verify.config.json`.

`<root>\VTMF.Settings.Mcp\appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } }
}
```

- [ ] **Step 2: Write the failing tests**

`<root>\VTMF.Settings.Tests\Mcp\McpTransportOptionTests.cs`:

```csharp
using VTMF.Settings.Mcp;

namespace VTMF.Settings.Tests.Mcp;

public sealed class McpTransportOptionTests
{
    [Theory]
    [InlineData(new string[0], McpTransport.Stdio)]
    [InlineData(new[] { "--transport", "stdio" }, McpTransport.Stdio)]
    [InlineData(new[] { "--transport", "http", "--urls", "http://0.0.0.0:8080" }, McpTransport.Http)]
    [InlineData(new[] { "--transport=HTTP" }, McpTransport.Http)]
    public void Parse_ReturnsTransport(string[] args, McpTransport expected) =>
        Assert.Equal(expected, McpTransportOption.Parse(args));

    [Fact]
    public void Parse_UnknownTransport_Throws() =>
        Assert.Throws<ArgumentException>(() => McpTransportOption.Parse(["--transport", "pigeon"]));
}
```

`<root>\VTMF.Settings.Tests\Mcp\SettingsReadToolsTests.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsReadToolsTests(SqlServerFixture fixture)
{
    private SettingsReadTools Tools => new(fixture.Sm, fixture.Connections);

    [Fact]
    public void ListClients_ReturnsConfiguredClients()
    {
        var result = Tools.ListClients();

        Assert.True(result.Ok);
        Assert.Equal([SqlServerFixture.ClientName, SqlServerFixture.EmptyClientName], result.Data!.Order(StringComparer.Ordinal).Reverse().ToArray());
    }

    [Fact]
    public async Task ModuleStatus_ClientWithoutModule_IsOkWithInstalledFalse()
    {
        var result = await Tools.ModuleStatus(SqlServerFixture.EmptyClientName, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(result.Data!.Installed);
    }

    [Fact]
    public async Task ListProcesses_ClientWithoutModule_ReturnsModuleNotInstalledError()
    {
        var result = await Tools.ListProcesses(SqlServerFixture.EmptyClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.ModuleNotInstalled, result.Error!.Code);
    }

    [Fact]
    public async Task GetSettings_UnknownClient_ReturnsUnknownClientError()
    {
        var result = await Tools.GetSettings("nosuchclient", "tproc", "DocumentListLayout", "base", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownClient, result.Error!.Code);
    }

    [Fact]
    public async Task GetSettings_InvalidLayer_ReturnsCodedError()
    {
        var result = await Tools.GetSettings(SqlServerFixture.ClientName, "tproc", "DocumentListLayout", "worker:bob", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.InvalidLayer, result.Error!.Code);
    }

    [Fact]
    public async Task GetEffectiveSettings_ReturnsRows()
    {
        var result = await Tools.GetEffectiveSettings(SqlServerFixture.ClientName, "tproc", "DocumentListLayout", "wterminal2", "en", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(10, result.Data!.Rows.Count);
    }

    [Fact]
    public async Task SettingsUsage_And_LegacyEnvRows_AreOk()
    {
        var usage = await Tools.SettingsUsage(SqlServerFixture.ClientName, "tproc", null, CancellationToken.None);
        var legacy = await Tools.LegacyEnvRows(SqlServerFixture.ClientName, "tproc", CancellationToken.None);
        var tables = await Tools.ListTables(SqlServerFixture.ClientName, "tproc", CancellationToken.None);

        Assert.True(usage.Ok && legacy.Ok && tables.Ok);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~Mcp"`
Expected: build FAIL — `McpTransportOption`, `SettingsReadTools`, `ToolErrorCodes` not defined.

- [ ] **Step 4: Implement transport option and tool result**

`<root>\VTMF.Settings.Mcp\McpTransportOption.cs`:

```csharp
namespace VTMF.Settings.Mcp;

public enum McpTransport
{
    Stdio,
    Http,
}

public static class McpTransportOption
{
    public static McpTransport Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? value = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--transport=", StringComparison.OrdinalIgnoreCase))
                value = args[i]["--transport=".Length..];
            else if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];
        }

        return value?.ToUpperInvariant() switch
        {
            null or "STDIO" => McpTransport.Stdio,
            "HTTP" => McpTransport.Http,
            _ => throw new ArgumentException($"Unknown transport '{value}'. Use stdio or http.", nameof(args)),
        };
    }
}
```

`<root>\VTMF.Settings.Mcp\ToolResult.cs`:

```csharp
using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Mcp;

public static class ToolErrorCodes
{
    public const string UnknownClient = "unknown_client";
}

public sealed record ToolError(string Code, string Message);

public sealed record ToolResult<T>(bool Ok, T? Data, ToolError? Error);

public static class ToolResult
{
    public static ToolResult<T> Success<T>(T data) => new(true, data, null);

    public static ToolResult<T> Failure<T>(string code, string message) => new(false, default, new ToolError(code, message));

    /// <summary>Runs a tool body and turns expected failures into structured results instead of server faults.</summary>
    public static async Task<ToolResult<T>> RunAsync<T>(Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return Success(await body().ConfigureAwait(false));
        }
        catch (SmException ex)
        {
            return Failure<T>(ex.Code, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Failure<T>(ToolErrorCodes.UnknownClient, ex.Message);
        }
    }
}
```

- [ ] **Step 5: Implement the tools**

`<root>\VTMF.Settings.Mcp\Tools\SettingsReadTools.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Server;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Services;

namespace VTMF.Settings.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "MCP tool types are discovered and invoked by the MCP SDK and tests.")]
public sealed class SettingsReadTools(SmClient sm, SqlConnectionProvider connections)
{
    private const string ClientDescription = "Client name from list_clients (one SQL database per client).";
    private const string ProcessDescription = "Process name (processtypes.NAME, lowercase), e.g. spaorderassembling.";
    private const string TableDescription = "View suffix such as ProcessSettings, DocumentListLayout, DocumentItemsListLayout, or ServerProcessSettings for backend settings.";

    [McpServerTool(Name = "list_clients", ReadOnly = true)]
    [Description("Lists configured VTMF clients. Every other tool takes one of these names as 'client'.")]
    public ToolResult<IReadOnlyList<string>> ListClients() =>
        ToolResult.Success<IReadOnlyList<string>>(connections.GetAvailableClients().ToList());

    [McpServerTool(Name = "module_status", ReadOnly = true)]
    [Description("Reports whether the settings management module (sm schema) is installed in the client database and its version. Tools other than list_clients need it.")]
    public Task<ToolResult<SmModuleStatus>> ModuleStatus(
        [Description(ClientDescription)] string client, CancellationToken cancellationToken) =>
        ToolResult.RunAsync(() => sm.GetModuleStatusAsync(client, cancellationToken));

    [McpServerTool(Name = "list_processes", ReadOnly = true)]
    [Description("Lists processes (processtypes): type, SETTINGPROCEDURES/DATAPROCEDURES aliases, variants that inherit settings from the process, and number of settings tables.")]
    public Task<ToolResult<IReadOnlyList<SmProcess>>> ListProcesses(
        [Description(ClientDescription)] string client, CancellationToken cancellationToken) =>
        ToolResult.RunAsync(() => sm.GetProcessesAsync(client, cancellationToken));

    [McpServerTool(Name = "list_tables", ReadOnly = true)]
    [Description("Lists the settings tables of a process per view and layer (base, env:{environment}, lang:{language}), with row counts and owner (the parent process when inherited via SETTINGPROCEDURES). overrideactive=true means a Get{process}{view}Info procedure serves this view instead of the table.")]
    public Task<ToolResult<IReadOnlyList<SmTable>>> ListTables(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        CancellationToken cancellationToken) =>
        ToolResult.RunAsync(() => sm.GetTablesAsync(client, process, cancellationToken));

    [McpServerTool(Name = "get_settings", ReadOnly = true)]
    [Description("Returns the raw rows stored in one settings table layer of the process itself (no inheritance, no overlay merge). ServerSetting values are always masked. Worker-level rows are not exposed: per-worker settings are discouraged; use environment or language layers.")]
    public Task<ToolResult<IReadOnlyList<SmSettingRow>>> GetSettings(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        [Description(TableDescription)] string table,
        [Description("Layer: base, env:{environment} or lang:{language code}.")] string layer = "base",
        [Description("Optional setting name prefix filter, e.g. Function.Barcode.")] string? namePrefix = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetSettingsAsync(client, process, table, layer, namePrefix, cancellationToken));

    [McpServerTool(Name = "get_effective_settings", ReadOnly = true)]
    [Description("Returns the settings as the terminal API would serve them: inheritance via SETTINGPROCEDURES, environment overlay, language overlay; each row reports the layer that supplied its value. If info.overrideprocedure is set, that procedure serves the view and the table is not what the terminal receives. For ServerProcessSettings, returns process values with parent fallback.")]
    public Task<ToolResult<SmEffectiveSettings>> GetEffectiveSettings(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        [Description(TableDescription)] string table,
        [Description("Optional environment name (e.g. wterminal2). wterminal or empty means base only.")] string? environment = null,
        [Description("Optional language code (e.g. en).")] string? language = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetEffectiveSettingsAsync(client, process, table, environment, language, cancellationToken));

    [McpServerTool(Name = "settings_usage", ReadOnly = true)]
    [Description("Backend settings catalogue for a process: which stored procedures read which ServerProcessSettings, the default in code at each call site, and the configured value. callstyle: psv (recommended bulk pattern), legacy (per-setting GetProcessSettingValue, to be migrated when the procedure changes), direct (reads the table directly, anti-pattern), unparsed (call the scanner could not read), none (configured but no reader found).")]
    public Task<ToolResult<IReadOnlyList<SmSettingUsage>>> SettingsUsage(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        [Description("Optional setting name prefix filter, e.g. Document.State.")] string? namePrefix = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetSettingsUsageAsync(client, process, namePrefix, cancellationToken));

    [McpServerTool(Name = "legacy_env_rows", ReadOnly = true)]
    [Description("Lists legacy environment overrides stored in base tables (workercode = environment name). The canonical place is the overlay table {table}{environment}; overlayexists tells whether it already exists.")]
    public Task<ToolResult<IReadOnlyList<SmLegacyEnvRow>>> LegacyEnvRows(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        CancellationToken cancellationToken) =>
        ToolResult.RunAsync(() => sm.GetLegacyEnvRowsAsync(client, process, cancellationToken));
}
```

If the SDK version's `McpServerToolAttribute` has no `ReadOnly` property, remove `ReadOnly = true` (it is only a hint).

- [ ] **Step 6: Implement `Program.cs`**

`<root>\VTMF.Settings.Mcp\Program.cs`:

```csharp
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Services;

var transport = McpTransportOption.Parse(args);

if (transport == McpTransport.Http)
{
    var builder = WebApplication.CreateBuilder(args);
    AddConfiguration(builder.Configuration);
    builder.Services.AddSqlConnectionParameters().AddSmClient();
    builder.Services.AddMcpServer().WithHttpTransport().WithTools<SettingsReadTools>();

    var app = builder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync().ConfigureAwait(false);
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    // stdout is the MCP channel in stdio mode: all logs go to stderr.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    AddConfiguration(builder.Configuration);
    builder.Services.AddSqlConnectionParameters().AddSmClient();
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<SettingsReadTools>();

    await builder.Build().RunAsync().ConfigureAwait(false);
}

static void AddConfiguration(IConfigurationBuilder configuration)
{
    // Same sources as VTMF.Settings: user secrets (developer machines) and environment variables
    // (Docker / settings.env). Environment variables win.
    configuration.AddUserSecrets(typeof(SettingsReadTools).Assembly, optional: true);
    configuration.AddEnvironmentVariables();
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj -Filter "FullyQualifiedName~Mcp"`
Expected: 12 passed (5 transport + 7 tools).

- [ ] **Step 8: Smoke-test stdio startup**

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Mode build -Target VTMF.Settings.Mcp/VTMF.Settings.Mcp.csproj
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' | dotnet C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings.Mcp\bin\Debug\net10.0\VTMF.Settings.Mcp.dll
```

Expected: one JSON line on stdout containing `"serverInfo"`; no log lines on stdout. Stop with Ctrl+C.

- [ ] **Step 9: Commit**

```powershell
git -C C:\PROJECTS\VTMF\Settingmanager add VTMF.Settings.Mcp VTMF.Settings.Tests VTMF.Settings\VTMF.Settings.sln eng\verify.config.json
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "feat(mcp): VTMF.Settings.Mcp server with read tools"
```

---

### Task 9: Deployment — Docker Compose service, installer set, usage docs

**Files:**
- Create: `<root>\Dockerfile.mcp`
- Modify: `<root>\compose.yaml`
- Modify: `<ui>\install.xml`
- Modify: `<ui>\README.md`

**Interfaces:**
- Consumes: `VTMF.Settings.Mcp` (`--transport http --urls ...`), set folder `<db5>\settingsmanagement`.
- Produces: compose service `vtmf-settings-mcp` on host port 8081 (MCP endpoint `http://localhost:8081/mcp`); install set `settingsmanagement`.

- [ ] **Step 1: Dockerfile for the MCP server**

`<root>\Dockerfile.mcp`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

COPY VTMF.SQLInstaller VTMF.SQLInstaller
COPY VTMF.Settings.Core VTMF.Settings.Core
COPY VTMF.Settings.Mcp VTMF.Settings.Mcp

RUN dotnet restore "VTMF.Settings.Mcp/VTMF.Settings.Mcp.csproj"
RUN dotnet publish "VTMF.Settings.Mcp/VTMF.Settings.Mcp.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=builder /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "VTMF.Settings.Mcp.dll", "--transport", "http", "--urls", "http://0.0.0.0:8080"]
```

- [ ] **Step 2: Compose service**

Append under `services:` in `<root>\compose.yaml` (same indentation as `vtmf-settings`):

```yaml
  vtmf-settings-mcp:
    build:
      context: .
      dockerfile: Dockerfile.mcp
    ports:
      - "8081:8080"
    env_file: settings.env
```

- [ ] **Step 3: Installer set**

In `<ui>\install.xml`, directly after the closing `</set>` of `<set name="base">`, add:

```xml
  <set name="settingsmanagement">
    <item name="settingsmanagement" check="database" isfolder="true" type="database" source="$/Sawmill/database5/settingsmanagement"/>
  </set>
```

Keep the `$/Sawmill/database5/...` form even though TFVC is retired. The installer maps it onto the local `database5` git checkout (`FileUtils` strips `$/` and `SqlScriptsPrefix`), takes script versions from `versions.csv`, and client install history is keyed by these paths, so all sets use one path convention. A script gets its `versions.csv` entry only from the `database5` pre-commit hook, so it must be committed there (Tasks 2–7) before the installer sees it. Nothing is checked into TFVC.

- [ ] **Step 4: Usage docs**

Append to `<ui>\README.md`:

```markdown
## Settings management MCP server (VTMF.Settings.Mcp)

Read-only MCP tools over the `sm` procedures (phase 1). Each client database needs the
`settingsmanagement` install set (Installer page) before its tools work; `module_status` reports it.

Connections are configured exactly like VTMF.Settings (`SqlConnectionParameters` via user secrets or
environment variables / `settings.env`). Modifications are attributed to the SQL login of the connection.

Local (stdio) — build once, then register the built dll (not `dotnet run`, whose build output would corrupt stdio):

    dotnet build VTMF.Settings.Mcp
    claude mcp add vtmf-settings -- dotnet C:\PROJECTS\VTMF\Settingmanager\VTMF.Settings.Mcp\bin\Debug\net10.0\VTMF.Settings.Mcp.dll

Docker (HTTP):

    docker compose up -d vtmf-settings-mcp
    claude mcp add --transport http vtmf-settings http://localhost:8081/mcp

Tools: list_clients, module_status, list_processes, list_tables, get_settings, get_effective_settings,
settings_usage, legacy_env_rows. ServerSetting values are always masked.
```

- [ ] **Step 5: Verify the image builds and serves**

```powershell
cd C:\PROJECTS\VTMF\Settingmanager
docker compose build vtmf-settings-mcp
docker compose up -d vtmf-settings-mcp
docker compose logs vtmf-settings-mcp --tail 20
docker compose build vtmf-settings
```

Expected: both images build; MCP logs show `Now listening on: http://0.0.0.0:8080`. Then `docker compose stop vtmf-settings-mcp`.

- [ ] **Step 6: Final verification**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` → Expected: `OK (gate)`.
Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Target VTMF.Settings.Tests/VTMF.Settings.Tests.csproj` → Expected: `OK (test)` (all tests incl. integration; Docker running).

- [ ] **Step 7: Commit and record task history**

Append to `<ui>\TASKHISTORY.md` (per project CLAUDE.md, task content as-is):

```markdown

### Task [014]: Settings management tools — phase 1 ✅ COMPLETED

Implement phase 1 of docs/superpowers/specs/2026-10-16-settings-management-tools-design.md (target net10.0)
following docs/superpowers/plans/2026-10-16-settings-management-phase1.md.
```

```powershell
git -C C:\PROJECTS\VTMF\Settingmanager add Dockerfile.mcp compose.yaml VTMF.Settings\install.xml VTMF.Settings\README.md
git -C C:\PROJECTS\VTMF\Settingmanager commit -m "chore: compose service, installer set and docs for the settings MCP server"
```

`TASKHISTORY.md` already had uncommitted changes before this work; leave it out of the commit and tell the user it was updated.

---

## Follow-up (separate task, required)

Moving the remaining Entities and Services into Core under the strict analyzer settings is its own task: `docs/superpowers/plans/2026-10-16-core-services-migration.md`. Phase 1 does not depend on it.
