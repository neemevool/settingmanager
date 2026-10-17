using System.Reflection;
using ModelContextProtocol.Server;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ModuleGateTests(SqlServerFixture fixture)
{
    public static TheoryData<string, string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            (string Client, string Code)[] states =
            [
                (SqlServerFixture.OutdatedClientName, "module_outdated"),
                (SqlServerFixture.NewerClientName, "module_newer_than_tool"),
                (SqlServerFixture.EmptyClientName, "module_not_installed"),
            ];
            foreach (var tool in ModuleGateCoverage.GatedCalls.Keys)
            {
                foreach (var (client, code) in states)
                    data.Add(tool, client, code);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GatedTool_RefusesUnlessUpToDate(string tool, string client, string expectedCode)
    {
        var tools = new SettingsReadTools(fixture.Sm, fixture.Connections);

        var error = await ModuleGateCoverage.GatedCalls[tool](tools, client);

        Assert.NotNull(error);
        Assert.Equal(expectedCode, error.Code);
    }
}

public sealed class ModuleGateCoverageTests
{
    [Fact]
    public void GatedCalls_CoverEveryReadTool()
    {
        var names = typeof(SettingsReadTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .Except(["list_clients", "module_status"], StringComparer.Ordinal);

        Assert.Equal(names.Order(StringComparer.Ordinal), ModuleGateCoverage.GatedCalls.Keys.Order(StringComparer.Ordinal));
    }
}

internal static class ModuleGateCoverage
{
    public static Dictionary<string, Func<SettingsReadTools, string, Task<ToolError?>>> GatedCalls { get; } = new(StringComparer.Ordinal)
    {
        ["list_processes"] = async (t, c) => (await t.ListProcesses(c, CancellationToken.None)).Error,
        ["list_tables"] = async (t, c) => (await t.ListTables(c, "tproc", CancellationToken.None)).Error,
        ["get_settings"] = async (t, c) => (await t.GetSettings(c, "tproc", "DocumentListLayout")).Error,
        ["get_effective_settings"] = async (t, c) => (await t.GetEffectiveSettings(c, "tproc", "DocumentListLayout")).Error,
        ["settings_usage"] = async (t, c) => (await t.SettingsUsage(c, "tproc")).Error,
        ["legacy_env_rows"] = async (t, c) => (await t.LegacyEnvRows(c, "tproc", CancellationToken.None)).Error,
    };
}
