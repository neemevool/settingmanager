using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>Typed access to the sm.* procedures of a client database. Holds no business rules.</summary>
public sealed class SmClient(SqlConnectionProvider connections)
{
    public async Task<SmModuleInfo?> GetModuleInfoAsync(string client, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<SmModuleInfo>(client, "sm.GetModuleInfo", null, cancellationToken).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<SmModuleStatus> GetModuleStatusAsync(string client, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetModuleInfoAsync(client, cancellationToken).ConfigureAwait(false);
            return new SmModuleStatus { Installed = info is not null, Version = info?.Version };
        }
        catch (SmException ex) when (ex.Code == SmErrorCodes.ModuleNotInstalled)
        {
            return new SmModuleStatus { Installed = false, Version = null };
        }
    }

    public Task<IReadOnlyList<SmProcess>> GetProcessesAsync(string client, CancellationToken cancellationToken = default) =>
        QueryAsync<SmProcess>(client, "sm.GetProcesses", null, cancellationToken);

    public Task<IReadOnlyList<SmTable>> GetTablesAsync(string client, string process, CancellationToken cancellationToken = default) =>
        QueryAsync<SmTable>(client, "sm.GetTables", new { process }, cancellationToken);

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string client, string procedure, object? parameters, CancellationToken cancellationToken)
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
