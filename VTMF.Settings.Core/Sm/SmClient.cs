using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Core.Sm.Usage;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>Typed access to the sm.* procedures of a client database. Holds no business rules.</summary>
public sealed class SmClient(SqlConnectionProvider connections, SmScriptSet scripts)
{
    private static readonly JsonSerializerOptions DesiredJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<SmModuleInfo?> GetModuleInfoAsync(string client, CancellationToken cancellationToken = default)
    {
        var rows = await QueryUngatedAsync<SmModuleInfo>(client, "sm.GetModuleInfo", null, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault(r => string.Equals(r.Module, scripts.ModuleName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SmModuleStatus> GetModuleStatusAsync(string client, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetModuleInfoAsync(client, cancellationToken).ConfigureAwait(false);
            return Status(info?.Version);
        }
        catch (SmException ex) when (ex.Code == SmErrorCodes.ModuleNotInstalled)
        {
            return Status(null);
        }
    }

    public Task<IReadOnlyList<SmProcess>> GetProcessesAsync(string client, CancellationToken cancellationToken = default) =>
        QueryAsync<SmProcess>(client, "sm.GetProcesses", null, cancellationToken);

    public Task<IReadOnlyList<SmTable>> GetTablesAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmTable>(client, "sm.GetTables", new { process }, cancellationToken);

    public Task<IReadOnlyList<SmSettingRow>> GetSettingsAsync(
        string client, string process, string table, string layer = "base", string? namePrefix = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync<SmSettingRow>(client, "sm.GetSettings", new { process, table, layer, namePrefix }, cancellationToken);

    public async Task<SmEffectiveSettings> GetEffectiveSettingsAsync(
        string client, string process, string table, string? environment = null, string? language = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(client, cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<SmSettingUsage>> GetSettingsUsageAsync(
        string client, string process, string? namePrefix = null, CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(client, cancellationToken).ConfigureAwait(false);
        await using var connection = connections.GetConnection(client);
        SmUsageSources sources;
        try
        {
            var command = new CommandDefinition("sm.GetSettingsUsage", new { process, namePrefix },
                commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
            using var grid = await connection.QueryMultipleAsync(command).ConfigureAwait(false);
            var processes = (await grid.ReadAsync<SmUsageProcess>().ConfigureAwait(false)).AsList();
            var modules = (await grid.ReadAsync<SmUsageModule>().ConfigureAwait(false)).AsList();
            var settings = (await grid.ReadAsync<SmUsageSetting>().ConfigureAwait(false)).AsList();
            sources = new SmUsageSources(processes, modules, settings);
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }

        return SettingsUsageBuilder.Build(process, namePrefix, sources);
    }

    public Task<IReadOnlyList<SmLegacyEnvRow>> GetLegacyEnvRowsAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmLegacyEnvRow>(client, "sm.GetLegacyEnvRows", new { process }, cancellationToken);

    public async Task<SmSession> StartSessionAsync(string client, string name, string process, CancellationToken cancellationToken = default) =>
        (await QueryAsync<SmSession>(client, "sm.StartSession", new { name, process }, cancellationToken).ConfigureAwait(false)).Single();

    public Task<IReadOnlyList<SmSession>> GetSessionsAsync(string client, string? process = null, int top = 20, CancellationToken cancellationToken = default) =>
        QueryAsync<SmSession>(client, "sm.GetSessions", new { process, top }, cancellationToken);

    public async Task<SmSession> CloseSessionAsync(string client, int sessionId, CancellationToken cancellationToken = default) =>
        (await QueryAsync<SmSession>(client, "sm.CloseSession", new { sessionid = sessionId }, cancellationToken).ConfigureAwait(false)).Single();

    public async Task<SmPlanResult> CreatePlanAsync(
        string client, SmDesiredState desired, string description, int? sessionId, SmPlanOptions? options = null,
        string source = "manual", bool dryRun = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desired);
        var parameters = new
        {
            desired = JsonSerializer.Serialize(desired, DesiredJson),
            description,
            source,
            options = options is null ? null : JsonSerializer.Serialize(options, DesiredJson),
            sessionid = sessionId,
            dryrun = dryRun,
        };

        return await QueryMultipleAsync(client, "sm.CreatePlan", parameters, ReadPlanResultAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates the inverse of an Applied plan (status Created); apply it with <see cref="ApplyPlanAsync"/>.</summary>
    public Task<SmPlanResult> CreateRollbackPlanAsync(string client, int planId, CancellationToken cancellationToken = default) =>
        QueryMultipleAsync(client, "sm.CreateRollbackPlan", new { planid = planId }, ReadPlanResultAsync, cancellationToken);

    /// <summary>Net change of the session's applied plans (first before-image, last after-image per row; reverted rows omitted).</summary>
    public Task<IReadOnlyList<SmPlanItem>> GetSessionChangesAsync(string client, int sessionId, CancellationToken cancellationToken = default) =>
        QueryAsync<SmPlanItem>(client, "sm.GetSessionChanges", new { sessionid = sessionId }, cancellationToken);

    /// <summary>Creates one plan reverting the session's net change (status Created, or NoChanges); apply it with <see cref="ApplyPlanAsync"/>.</summary>
    public Task<SmPlanResult> CreateSessionRollbackPlanAsync(string client, int sessionId, CancellationToken cancellationToken = default) =>
        QueryMultipleAsync(client, "sm.CreateSessionRollbackPlan", new { sessionid = sessionId }, ReadPlanResultAsync, cancellationToken);

    /// <summary>
    /// Creates a plan that moves legacy environment rows (base rows with workercode other than ALL) into their overlay
    /// tables (status Created, or NoChanges); apply it with <see cref="ApplyPlanAsync"/>.
    /// </summary>
    public Task<SmPlanResult> CreateLegacyEnvMigrationPlanAsync(string client, string process, int sessionId, CancellationToken cancellationToken = default) =>
        QueryMultipleAsync(client, "sm.CreateLegacyEnvMigrationPlan", new { process, sessionid = sessionId }, ReadPlanResultAsync, cancellationToken);

    public Task<SmPlanDetails> GetPlanAsync(string client, int planId, CancellationToken cancellationToken = default) =>
        QueryMultipleAsync(client, "sm.GetPlan", new { planid = planId }, async grid =>
        {
            var plan = await grid.ReadSingleAsync<SmPlan>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<SmPlanItem>().ConfigureAwait(false)).AsList();
            var warnings = (await grid.ReadAsync<SmPlanWarning>().ConfigureAwait(false)).AsList();
            return new SmPlanDetails { Plan = plan, Items = items, Warnings = warnings };
        }, cancellationToken);

    /// <summary>Applies a Created plan. Stale and Failed are returned as status, not thrown.</summary>
    public Task<SmApplyResult> ApplyPlanAsync(string client, int planId, string checksum, CancellationToken cancellationToken = default) =>
        QueryMultipleAsync(client, "sm.ApplyPlan", new { planid = planId, checksum }, async grid =>
        {
            var header = await grid.ReadSingleAsync<ApplyHeader>().ConfigureAwait(false);
            var conflicts = (await grid.ReadAsync<SmPlanConflict>().ConfigureAwait(false)).AsList();
            return new SmApplyResult { PlanId = header.PlanId, Status = header.Status, Message = header.Message, Conflicts = conflicts };
        }, cancellationToken);

    public Task<IReadOnlyList<SmPlan>> GetPlansAsync(
        string client, string? process = null, int? sessionId = null, int top = 20, CancellationToken cancellationToken = default) =>
        QueryAsync<SmPlan>(client, "sm.GetPlans", new { process, sessionid = sessionId, top }, cancellationToken);

    public Task<IReadOnlyList<SmAppliedFeature>> GetAppliedFeaturesAsync(
        string client, string? process = null, CancellationToken cancellationToken = default) =>
        QueryAsync<SmAppliedFeature>(client, "sm.GetAppliedFeatures", new { process }, cancellationToken);

    private SmModuleStatus Status(int? version) => new()
    {
        Installed = version is not null,
        Version = version,
        AvailableVersion = scripts.Version,
        State = SmModuleStates.From(version, scripts.Version),
    };

    /// <summary>Central version gate: every query except module info/status goes through here.</summary>
    private async Task EnsureUpToDateAsync(string client, CancellationToken cancellationToken)
    {
        var status = await GetModuleStatusAsync(client, cancellationToken).ConfigureAwait(false);
        switch (status.State)
        {
            case SmModuleStates.UpToDate:
                return;
            case SmModuleStates.NewerThanTool:
                throw new SmException(SmErrorCodes.ModuleNewerThanTool,
                    SmMessages.NewerThanTool(client, status.Version, status.AvailableVersion));
            case SmModuleStates.Outdated:
                throw new SmException(SmErrorCodes.ModuleOutdated,
                    $"The sm module in client '{client}' is outdated (installed version {status.Version}, available {status.AvailableVersion}); call install_module for client '{client}'.");
            default:
                throw new SmException(SmErrorCodes.ModuleNotInstalled,
                    $"The sm module is not installed in client '{client}' (available version {status.AvailableVersion}); call install_module for client '{client}'.");
        }
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string client, string procedure, object? parameters, CancellationToken cancellationToken)
    {
        await EnsureUpToDateAsync(client, cancellationToken).ConfigureAwait(false);
        return await QueryUngatedAsync<T>(client, procedure, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> QueryMultipleAsync<T>(
        string client, string procedure, object parameters, Func<SqlMapper.GridReader, Task<T>> read, CancellationToken cancellationToken)
    {
        await EnsureUpToDateAsync(client, cancellationToken).ConfigureAwait(false);
        await using var connection = connections.GetConnection(client);
        try
        {
            var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
            using var grid = await connection.QueryMultipleAsync(command).ConfigureAwait(false);
            return await read(grid).ConfigureAwait(false);
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }

    private async Task<IReadOnlyList<T>> QueryUngatedAsync<T>(string client, string procedure, object? parameters, CancellationToken cancellationToken)
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

    /// <summary>Maps the three result sets of sm.CreatePlan and of the procedures returning the same shape (sm.CreateRollbackPlan, sm.CreateSessionRollbackPlan).</summary>
    private static async Task<SmPlanResult> ReadPlanResultAsync(SqlMapper.GridReader grid)
    {
        var header = await grid.ReadSingleAsync<PlanHeader>().ConfigureAwait(false);
        var items = (await grid.ReadAsync<SmPlanItem>().ConfigureAwait(false)).AsList();
        var warnings = (await grid.ReadAsync<SmPlanWarning>().ConfigureAwait(false)).AsList();
        return new SmPlanResult
        {
            PlanId = header.PlanId,
            Checksum = header.Checksum,
            Status = header.Status,
            Items = items,
            Warnings = warnings,
        };
    }

    private sealed class PlanHeader
    {
        public int? PlanId { get; set; }
        public string? Checksum { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ApplyHeader
    {
        public int PlanId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
    }
}
