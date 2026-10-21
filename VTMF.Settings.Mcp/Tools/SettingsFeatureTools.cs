using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Server;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;

namespace VTMF.Settings.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "MCP tool types are discovered and invoked by the MCP SDK and tests.")]
public sealed class SettingsFeatureTools(SmFeatureStore features, SmFeatureInstaller installer, SmFeaturePlanner planner, SmClient sm)
{
    private const string ClientDescription = "Client name from list_clients (one SQL database per client).";
    private const string RevisionDescription = "Revision of the feature as last read (get_feature or the last save result); a stale revision fails with feature_changed.";

    [McpServerTool(Name = "feature_module_status", ReadOnly = true)]
    [Description("Reports whether the feature module (settingsmetabase, shared metadata database) is installed, outdated or newer than this tool. install_feature_module fixes not_installed and outdated.")]
    public Task<ToolResult<SmModuleStatus>> FeatureModuleStatus(CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => features.GetModuleStatusAsync(cancellationToken));

    [McpServerTool(Name = "install_feature_module", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Installs or upgrades the feature module (sm schema in the shared metadata database) from the scripts shipped with this server. Safe to re-run; never downgrades.")]
    public Task<ToolResult<SmInstallResult>> InstallFeatureModule(CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => installer.InstallAsync(cancellationToken));

    [McpServerTool(Name = "find_features", ReadOnly = true)]
    [Description("Searches the feature catalog by text (matches name, description, built-on process and row (setting) names). Only Finished features can be planned; status defaults to Finished, use Draft for work in progress or 'all' for both. Returns id, name, what the feature enables for the end user, status, the settings table kinds it touches and its parameter count.")]
    public Task<ToolResult<IReadOnlyList<SmFeatureSummary>>> FindFeatures(
        [Description("Optional text to match in the feature name, description, built-on process or row (setting) names.")] string? text = null,
        [Description("Draft, Finished (default) or all.")] string? status = "Finished",
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => features.FindFeaturesAsync(
            text, string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) ? null : status ?? "Finished", cancellationToken));

    [McpServerTool(Name = "get_feature", ReadOnly = true)]
    [Description("Returns one feature document by id or by name: description, status, revision, the rows it sets, its parameters and its manual steps. Rows and values may contain {{param}} placeholders; {{processname}} and {{clientname}} are built in.")]
    public Task<ToolResult<SmFeature>> GetFeature(
        [Description("Feature id from find_features.")] int? id = null,
        [Description("Feature name (used when id is omitted).")] string? name = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => features.GetFeatureAsync(id, name, cancellationToken));

    [McpServerTool(Name = "save_feature", ReadOnly = false, Destructive = false)]
    [Description("Creates a feature (no id; saved as Draft) or replaces an existing one: the whole feature is replaced, so pass every row, parameter and step, and the last revision. " +
        "The description must say what the feature enables for the end user, not how it is built. " +
        "Feature fields: name, description, builtOnProcess, rows [{ table, layer, name, value, settingtype: Setting|SettingGroup, listvalue, op: set|remove, after, note }], params [{ name, description, type: Text|Number|Boolean|List, default, values: [{ value, description }] }], steps [{ text, sql }]. " +
        "Use {{param}} placeholders in row values; {{processname}} and {{clientname}} are built in. Steps are manual follow-up work; step sql is shown to the user and never executed. " +
        "Returns id, the new revision and warnings (for example PARAM_UNUSED). Finish the feature with set_feature_status before planning it.")]
    public Task<ToolResult<SmFeatureSaveResult>> SaveFeature(
        [Description("The complete feature document.")] SmFeature feature,
        [Description("Last known revision; omit when creating a new feature.")] long? revision = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => features.SaveFeatureAsync(feature, revision, cancellationToken));

    [McpServerTool(Name = "set_feature_status", ReadOnly = false, Destructive = false)]
    [Description("Sets a feature to Finished (validated: needs a description, parameter descriptions and consistent placeholders) or back to Draft. Only Finished features can be planned. Returns the new revision and warnings.")]
    public Task<ToolResult<SmFeatureSaveResult>> SetFeatureStatus(
        [Description("Feature id.")] int id,
        [Description("Draft or Finished.")] string status,
        [Description(RevisionDescription)] long revision,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => features.SetFeatureStatusAsync(id, status, revision, cancellationToken));

    [McpServerTool(Name = "delete_feature", ReadOnly = false, Destructive = true)]
    [Description("Deletes a feature from the catalog. Only Draft features can be deleted; set_feature_status to Draft first. Settings already applied from it are not touched. Ask the user first.")]
    public Task<ToolResult<bool>> DeleteFeature(
        [Description("Feature id.")] int id,
        [Description(RevisionDescription)] long revision,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(async () =>
        {
            await features.DeleteFeatureAsync(id, revision, cancellationToken).ConfigureAwait(false);
            return true;
        });

    [McpServerTool(Name = "feature_plan", ReadOnly = false, Destructive = false)]
    [Description("Stores a plan that applies a Finished feature to one process and changes no settings: drafts are refused with feature_not_finished. Ask the user for every parameter without a default (a missing one fails with feature_param_missing). " +
        "Show the plan items, warnings (for example SETTING_NOT_READ: the process code does not read a server setting the feature sets) and steps to the user, and call apply_plan only after the user confirms. " +
        "The result also lists the manual steps: show their sql to the user to run; the assistant never executes step sql. Fails with session_closed if the session is closed.")]
    public Task<ToolResult<SmFeaturePlanResult>> FeaturePlan(
        [Description(ClientDescription)] string client,
        [Description("Feature id from find_features.")] int featureId,
        [Description("Process name (processtypes.NAME, lowercase), e.g. spaorderassembling.")] string process,
        [Description("Session id from start_session.")] int sessionId,
        [Description("Parameter values by parameter name; omit to use defaults.")] Dictionary<string, string>? parameters = null,
        [Description("Optional plan options: createTable (true allows creating a missing settings table or layer table) and targetAlias (true allows writing to a process whose settings are an alias of another process).")] SmPlanOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => planner.FeaturePlanAsync(client, featureId, process, sessionId, parameters, options, cancellationToken));

    [McpServerTool(Name = "list_applied_features", ReadOnly = true)]
    [Description("Lists the features applied on a client (not rolled back), optionally for one process, with the feature's current name, description and status; those are null when the feature no longer exists in the catalog.")]
    public Task<ToolResult<IReadOnlyList<SmAppliedFeatureInfo>>> ListAppliedFeatures(
        [Description(ClientDescription)] string client,
        [Description("Optional process filter.")] string? process = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(async () =>
        {
            var applied = await sm.GetAppliedFeaturesAsync(client, process, cancellationToken).ConfigureAwait(false);
            if (applied.Count == 0)
            {
                return (IReadOnlyList<SmAppliedFeatureInfo>)[];
            }

            var catalog = (await features.FindFeaturesAsync(null, null, cancellationToken).ConfigureAwait(false))
                .ToDictionary(f => f.Id);
            return applied.Select(a =>
            {
                catalog.TryGetValue(a.FeatureId, out var current);
                return new SmAppliedFeatureInfo
                {
                    Applied = a,
                    Description = current?.Description,
                    CurrentName = current?.Name,
                    Status = current?.Status,
                };
            }).ToList();
        });
}
