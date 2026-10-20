using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;

namespace VTMF.Settings.Tests.Mcp;

public sealed class McpServerSetupTests
{
    private static readonly string[] PlanToolNames =
    [
        "start_session", "list_sessions", "session_changes", "close_session", "create_plan", "get_plan", "list_plans",
        "apply_plan", "rollback_plan", "rollback_session", "compare_clients", "clone_plan", "legacy_env_migration_plan",
    ];

    [Fact]
    public void AddSettingsMcpServer_SetsInstructions()
    {
        var services = new ServiceCollection();
        services.AddSettingsMcpServer();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(McpServerSetup.Instructions, options.ServerInstructions);
        Assert.Contains("call apply_plan only after the user confirms", McpServerSetup.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSettingsMcpServer_RegistersAllPlanTools()
    {
        var names = PlanToolAttributes().Select(a => a.Name).ToList();

        Assert.Equal(PlanToolNames.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ApplyPlan_IsTheOnlyDestructivePlanTool()
    {
        var destructive = PlanToolAttributes().Where(a => a.Destructive).Select(a => a.Name).ToList();

        Assert.Equal(["apply_plan"], destructive);
    }

    [Fact]
    public void AddSettingsMcpServer_RegistersPlanToolsWithServer()
    {
        var services = new ServiceCollection();
        services.AddSettingsMcpServer();
        using var provider = services.BuildServiceProvider();

        var registered = provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToList();

        foreach (var name in PlanToolNames)
        {
            Assert.Contains(name, registered);
        }
    }

    private static IEnumerable<(string Name, bool Destructive)> PlanToolAttributes() =>
        typeof(SettingsPlanTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .OfType<McpServerToolAttribute>()
            .Select(a => (a.Name!, !a.ReadOnly && a.Destructive));
}
