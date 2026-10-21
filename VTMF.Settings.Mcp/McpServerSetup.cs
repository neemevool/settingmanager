using ModelContextProtocol.Server;
using VTMF.Settings.Mcp.Tools;

namespace VTMF.Settings.Mcp;

public static class McpServerSetup
{
    public const string Instructions =
        "Before working with a client, call module_status. If state is not_installed or outdated, call install_module for that client, then continue. " +
        "If state is newer_than_tool, stop and tell the user the VTMF.Settings MCP server image must be updated. " +
        "To change settings, start a session, create a plan, show its items to the user, and call apply_plan only after the user confirms; never change settings any other way. " +
        "Feature tools use the shared metadata database: if a feature tool returns feature_module_not_installed or feature_module_outdated, call install_feature_module, then continue; feature_module_newer_than_tool means the MCP server image must be updated. " +
        "Tools return errors as { ok: false, error: { code, message } }; codes module_not_installed and module_outdated mean install_module must be run first.";

    public static IMcpServerBuilder AddSettingsMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddMcpServer(options => options.ServerInstructions = Instructions)
            .WithTools<SettingsReadTools>()
            .WithTools<SettingsModuleTools>()
            .WithTools<SettingsPlanTools>()
            .WithTools<SettingsFeatureTools>();
    }
}
