using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>Typed access to the sm.* procedures of a client database. Holds no business rules.</summary>
public sealed class SmClient(SqlConnectionProvider connections, SmScriptSet scripts)
{
    public async Task<SmModuleInfo?> GetModuleInfoAsync(string client, CancellationToken cancellationToken = default)
    {
        var rows = await QueryUngatedAsync<SmModuleInfo>(client, "sm.GetModuleInfo", null, cancellationToken).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
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

    public Task<IReadOnlyList<SmSettingUsage>> GetSettingsUsageAsync(
        string client, string process, string? namePrefix = null, CancellationToken cancellationToken = default) =>
        QueryAsync<SmSettingUsage>(client, "sm.GetSettingsUsage", new { process, namePrefix }, cancellationToken);

    public Task<IReadOnlyList<SmLegacyEnvRow>> GetLegacyEnvRowsAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmLegacyEnvRow>(client, "sm.GetLegacyEnvRows", new { process }, cancellationToken);

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
}
