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
    [Description("Reports the settings management module (sm schema) state in the client database: not_installed, up_to_date, outdated or newer_than_tool, plus its version. Call this first; if not_installed or outdated, run install_module. Tools other than list_clients and install_module need a current module.")]
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

