using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Server;
using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "MCP tool types are discovered and invoked by the MCP SDK and tests.")]
public sealed class SettingsModuleTools(SmInstaller installer)
{
    [McpServerTool(Name = "install_module", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Installs or upgrades the settings management module (sm schema) in the client database from the scripts shipped with this server. Safe to re-run; never downgrades.")]
    public Task<ToolResult<SmInstallResult>> InstallModule(
        [Description("Client name from list_clients (one SQL database per client).")] string client,
        CancellationToken cancellationToken) =>
        ToolResult.RunAsync(() => installer.InstallAsync(client, cancellationToken));
}
