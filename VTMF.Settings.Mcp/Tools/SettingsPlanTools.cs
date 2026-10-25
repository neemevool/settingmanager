using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Server;
using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "MCP tool types are discovered and invoked by the MCP SDK and tests.")]
public sealed class SettingsPlanTools(SmClient sm, SmOrchestrator orchestrator)
{
    private const string ClientDescription = "Client name from list_clients (one SQL database per client).";
    private const string ProcessDescription = "Process name (processtypes.NAME, lowercase), e.g. spaorderassembling.";
    private const string SessionDescription = "Session id from start_session or list_sessions.";
    private const string PlanDescription = "Plan id from a plan result or list_plans.";
    private const string ScopeDescription = "Optional scope: list of { table, layer } entries limiting which settings tables/layers are compared or cloned; omit for everything the process owns. Layer: base, env:{environment} or lang:{language code}.";
    private const string OptionsDescription = "Optional plan options: createTable (true allows creating a missing settings table or layer table) and targetAlias (true allows writing to a process whose settings are an alias of another process, which changes the shared settings).";
    private const string NotChanging = "Stores a plan and changes no settings: show the plan items and warnings to the user, then call apply_plan only after the user confirms. ";

    [McpServerTool(Name = "start_session", ReadOnly = false, Destructive = false)]
    [Description("Starts a change session: a named group of plans for one process, used to review and roll back everything done in one piece of work. Changes no settings. Start a session before create_plan; close it with close_session when the work is finished.")]
    public Task<ToolResult<SmSession>> StartSession(
        [Description(ClientDescription)] string client,
        [Description("Short human-readable name of the piece of work, e.g. 'Barcode column layout'.")] string name,
        [Description(ProcessDescription)] string process,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.StartSessionAsync(client, name, process, cancellationToken));

    [McpServerTool(Name = "list_sessions", ReadOnly = true)]
    [Description("Lists change sessions, newest first, optionally for one process.")]
    public Task<ToolResult<IReadOnlyList<SmSession>>> ListSessions(
        [Description(ClientDescription)] string client,
        [Description("Optional process filter.")] string? process = null,
        [Description("Maximum number of sessions to return.")] int top = 20,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetSessionsAsync(client, process, top, cancellationToken));

    [McpServerTool(Name = "session_changes", ReadOnly = true)]
    [Description("Returns the net change of the session's applied plans: per row the first before-image and last after-image; rows that were changed and reverted are omitted. Use it to review a session before closing it or rolling it back.")]
    public Task<ToolResult<IReadOnlyList<SmPlanItem>>> SessionChanges(
        [Description(ClientDescription)] string client,
        [Description(SessionDescription)] int sessionId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetSessionChangesAsync(client, sessionId, cancellationToken));

    [McpServerTool(Name = "close_session", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Closes a session: create_plan, clone_plan and legacy_env_migration_plan then return session_closed for it; rollback_plan and rollback_session still work on its plans. Changes no settings. Safe to repeat.")]
    public Task<ToolResult<SmSession>> CloseSession(
        [Description(ClientDescription)] string client,
        [Description(SessionDescription)] int sessionId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CloseSessionAsync(client, sessionId, cancellationToken));

    [McpServerTool(Name = "create_plan", ReadOnly = false, Destructive = false)]
    [Description(NotChanging +
        "Builds a plan from a desired state. Desired state fields: process; mode ('merge' = only the listed rows are inserted/updated/deleted, everything else is left alone; 'replace' = the listed rows become the complete content of every table/layer in scope, rows not listed are deleted, and every row must then carry ordernr); " +
        "scope (list of { table, layer }; required for replace, it names the tables/layers that are replaced); " +
        "rows (list of { table, layer, name, value, settingtype, listvalue, ordernr, op, after }). " +
        "table is the view suffix such as DocumentListLayout, ProcessSettings or ServerProcessSettings. " +
        "layer is base, env:{environment} or lang:{language code}; worker-level rows are never written (workercode is not accepted), use environment or language layers. " +
        "settingtype is Setting (default) or SettingGroup; ServerSetting values are never available, never shown and cannot be written. " +
        "op is 'upsert' (default), 'remove' (deletes the row) or 'move' (repositions an existing SettingGroup row and its block without changing values; group rows only, 'after' required, merge mode only, not ServerProcessSettings; a missing group fails with group_not_found). value and listvalue are the new values. " +
        "ordernr is the sort position; when omitted in merge mode the row is placed by 'after' or appended; do not give ordernr on SettingGroup rows in merge mode (group_ordernr). " +
        "after anchors the row after another existing row, given as NAME or NAME=VALUE. " +
        "Groups: a SettingGroup row and the rows under it (NAME.VALUE. e.g. Column.DOCNR., or Detail.X. for a group named Detail.X) are placed, moved and removed as one block. " +
        "For a SettingGroup row 'after' names a sibling group (Column=DOCNR, Detail.Lines) or '^' for first; a missing anchor refuses the plan (group_anchor_not_found) and an anchor that is not a sibling group too (group_anchor_invalid). " +
        "Existing group rows are not moved by upsert (warning GROUP_NOT_MOVED): use op move. " +
        "remove of a group removes its block, and its rows in language tables (unless the group remains in another base/env table); a plan that removes a group and also upserts or moves it or its rows is refused (group_remove_conflict). " +
        "Warnings to relay: ANCHOR_OUTSIDE_GROUP (a new row of a group was anchored outside its group; it is placed at the end of its group), RENUMBER (no free ordernr where the rows go; the table is renumbered in steps of 10 and existing rows change ordernr), and SPLIT_GROUP (a group's rows are not contiguous: suggest renumber_plan). " +
        "Options: createTable allows creating a missing table or layer table; targetAlias allows writing where the process settings are an alias of another process. " +
        "The result has planId, checksum, status (Created, NoChanges), items and warnings; pass planId and checksum to apply_plan after the user confirms the items. Fails with session_closed if the session is closed.")]
    public Task<ToolResult<SmPlanResult>> CreatePlan(
        [Description(ClientDescription)] string client,
        [Description(SessionDescription)] int sessionId,
        [Description("Desired state: { process, mode: merge|replace, scope: [{ table, layer }], rows: [{ table, layer, name, value, settingtype, listvalue, ordernr, op: upsert|remove|move, after }] }. op move: SettingGroup rows only, after required. after: NAME or NAME=VALUE; for a SettingGroup row also ^ (first of its family); ^ is invalid on any other row.")] SmDesiredState desired,
        [Description("Human-readable description of the change, stored with the plan and shown in history.")] string description,
        [Description(OptionsDescription)] SmPlanOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CreatePlanAsync(client, desired, description, sessionId, options, "manual", false, cancellationToken));

    [McpServerTool(Name = "renumber_plan", ReadOnly = false, Destructive = false)]
    [Description(NotChanging +
        "Builds a plan that tidies the ordernr of one existing settings table/layer of the process: every SettingGroup row and the rows under it are made one contiguous block, in the current group order, and ordernr is renumbered evenly. Values are never changed, only ordernr. " +
        "Use it when create_plan warns SPLIT_GROUP or the rows of a group are scattered. Rows that belong to no group (orphans) are placed last and reported with warning ORPHAN_ROWS. " +
        "Works on existing own tables only: it cannot create a table or write through an alias (a missing table fails with TABLE_MISSING, an alias-only table with ALIAS_TARGET; ignore any createTable/targetAlias hint in those messages). ServerProcessSettings is refused (invalid_desired), as are duplicate rows (duplicate_row). " +
        "The result has planId, checksum, status (Created, NoChanges), items and warnings; apply it with apply_plan after the user confirms. Fails with session_closed if the session is closed.")]
    public Task<ToolResult<SmPlanResult>> RenumberPlan(
        [Description(ClientDescription)] string client,
        [Description(SessionDescription)] int sessionId,
        [Description(ProcessDescription)] string process,
        [Description("Settings table (view suffix) to renumber, e.g. DocumentListLayout.")] string table,
        [Description("Layer to renumber: base, env:{environment} or lang:{language code}.")] string layer,
        [Description("Human-readable description of the change; defaults to 'Renumber {table} ({layer})'.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CreateRenumberPlanAsync(client, process, table, layer, string.IsNullOrWhiteSpace(description) ? $"Renumber {table} ({layer})" : description, sessionId, false, cancellationToken));

    [McpServerTool(Name = "get_plan", ReadOnly = true)]
    [Description("Returns a stored plan with its items and warnings, including its current status (Created, Applied, Failed, Stale, RolledBack).")]
    public Task<ToolResult<SmPlanDetails>> GetPlan(
        [Description(ClientDescription)] string client,
        [Description(PlanDescription)] int planId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetPlanAsync(client, planId, cancellationToken));

    [McpServerTool(Name = "list_plans", ReadOnly = true)]
    [Description("Lists stored plans, newest first, optionally for one process or one session.")]
    public Task<ToolResult<IReadOnlyList<SmPlan>>> ListPlans(
        [Description(ClientDescription)] string client,
        [Description("Optional process filter.")] string? process = null,
        [Description("Optional session filter.")] int? sessionId = null,
        [Description("Maximum number of plans to return.")] int top = 20,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.GetPlansAsync(client, process, sessionId, top, cancellationToken));

    [McpServerTool(Name = "apply_plan", ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("The only tool that changes settings. Call it only after the user confirmed the items shown from create_plan (or get_plan); never apply a plan the user has not seen. " +
        "Requires the plan's checksum; a wrong checksum fails with checksum_mismatch, a plan that is not in status Created fails with plan_status. " +
        "Returns status Applied, Stale or Failed (not errors). Stale means settings changed since the preview: nothing was applied, conflicts lists the differing rows; re-read the settings, rebuild the plan with create_plan, show it again and ask again; never apply partially or work around it. " +
        "Failed means the apply was rolled back as a whole; report the message. " +
        "After Applied, tell the user to reload the SPA (terminal web app) to see the new settings; to undo, use rollback_plan or rollback_session.")]
    public Task<ToolResult<SmApplyResult>> ApplyPlan(
        [Description(ClientDescription)] string client,
        [Description(PlanDescription)] int planId,
        [Description("The checksum returned together with the plan (plan result or get_plan).")] string checksum,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.ApplyPlanAsync(client, planId, checksum, cancellationToken));

    [McpServerTool(Name = "rollback_plan", ReadOnly = false, Destructive = false)]
    [Description(NotChanging + "Creates a plan that reverts one applied plan (the plan must be in status Applied). Apply the returned plan with apply_plan after confirmation.")]
    public Task<ToolResult<SmPlanResult>> RollbackPlan(
        [Description(ClientDescription)] string client,
        [Description("Id of the applied plan to revert.")] int planId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CreateRollbackPlanAsync(client, planId, cancellationToken));

    [McpServerTool(Name = "rollback_session", ReadOnly = false, Destructive = false)]
    [Description(NotChanging + "Creates one plan that reverts the net change of all applied plans of the session (status Created, or NoChanges). Apply the returned plan with apply_plan after confirmation.")]
    public Task<ToolResult<SmPlanResult>> RollbackSession(
        [Description(ClientDescription)] string client,
        [Description(SessionDescription)] int sessionId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CreateSessionRollbackPlanAsync(client, sessionId, cancellationToken));

    [McpServerTool(Name = "compare_clients", ReadOnly = true)]
    [Description("Dry run: compares one process's settings between two clients and returns the items that would make the target equal to the source (status DryRun, or NoChanges). Stores nothing and changes nothing; ServerSetting values are never compared or shown. Use clone_plan to turn the difference into a plan.")]
    public Task<ToolResult<SmPlanResult>> CompareClients(
        [Description("Client to read settings from.")] string source,
        [Description("Client the settings would be written to.")] string target,
        [Description(ProcessDescription)] string process,
        [Description(ScopeDescription)] IReadOnlyList<SmScopeEntry>? scope = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => orchestrator.CompareClientsAsync(source, target, process, scope, cancellationToken));

    [McpServerTool(Name = "clone_plan", ReadOnly = false, Destructive = false)]
    [Description(NotChanging + "Creates a plan on the target client (in a session of the target) that makes the process's settings equal to the source client's. The session id belongs to the target client. ServerSetting values are never copied. Apply with apply_plan on the target client.")]
    public Task<ToolResult<SmPlanResult>> ClonePlan(
        [Description("Client to read settings from.")] string source,
        [Description("Client the plan is stored on and applied to.")] string target,
        [Description(ProcessDescription)] string process,
        [Description("Open session on the target client (start_session on the target).")] int sessionId,
        [Description(ScopeDescription)] IReadOnlyList<SmScopeEntry>? scope = null,
        [Description(OptionsDescription)] SmPlanOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => orchestrator.ClonePlanAsync(source, target, process, sessionId, scope, options, cancellationToken));

    [McpServerTool(Name = "legacy_env_migration_plan", ReadOnly = false, Destructive = false)]
    [Description(NotChanging + "Creates a plan that moves legacy environment rows (base-table rows whose workercode is not ALL) into the environment overlay tables (status Created, or NoChanges). Apply with apply_plan after confirmation.")]
    public Task<ToolResult<SmPlanResult>> LegacyEnvMigrationPlan(
        [Description(ClientDescription)] string client,
        [Description(ProcessDescription)] string process,
        [Description(SessionDescription)] int sessionId,
        CancellationToken cancellationToken = default) =>
        ToolResult.RunAsync(() => sm.CreateLegacyEnvMigrationPlanAsync(client, process, sessionId, cancellationToken));
}
