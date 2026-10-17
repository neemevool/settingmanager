using ModelContextProtocol.Server;
using VTMF.Settings.Mcp.Tools;

namespace VTMF.Settings.Mcp;

public static class McpServerSetup
{
    public const string Instructions =
        "Before working with a client, call module_status. If state is not_installed or outdated, call install_module for that client, then continue. " +
        "If state is newer_than_tool, stop and tell the user the VTMF.Settings MCP server image must be updated. " +
        "Tools return errors as { ok: false, error: { code, message } }; codes module_not_installed and module_outdated mean install_module must be run first.";

    public static IMcpServerBuilder AddSettingsMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddMcpServer(options => options.ServerInstructions = Instructions)
            .WithTools<SettingsReadTools>()
            .WithTools<SettingsModuleTools>();
    }
}
