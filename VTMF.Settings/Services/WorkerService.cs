using Dapper;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class WorkerService(SqlConnectionProvider sqlConnectionProvider) : IWorkerService
{
    private const string SelectColumns = """
        workercode AS WorkerCode,
        primarystock AS PrimaryStock,
        stocks AS Stocks,
        processtag AS ProcessTag,
        processnames AS ProcessNames,
        primarydepartment AS PrimaryDepartment,
        processgroup AS ProcessGroup
        """;

    public async Task<IEnumerable<WorkerProperty>> GetAllAsync(ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        var sql = $"SELECT {SelectColumns} FROM workerproperty";
        return await connection.QueryAsync<WorkerProperty>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<WorkerProperty?> GetByWorkerCodeAsync(ClientInfo client, string workerCode, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        var sql = $"SELECT {SelectColumns} FROM workerproperty WHERE workercode = @workerCode";
        return await connection.QuerySingleOrDefaultAsync<WorkerProperty>(
            new CommandDefinition(sql, new { workerCode }, cancellationToken: ct));
    }

    public async Task<(bool success, string message)> CreateAsync(ClientInfo client, WorkerProperty worker, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(worker.WorkerCode))
            return (false, "Worker code is required.");

        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);

        const string existsSql = "SELECT COUNT(1) FROM workerproperty WHERE workercode = @WorkerCode";
        var exists = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(existsSql, new { worker.WorkerCode }, cancellationToken: ct));
        if (exists > 0)
            return (false, $"Worker '{worker.WorkerCode}' already exists.");

        const string insertSql = """
            INSERT INTO workerproperty
                (workercode, primarystock, stocks, processtag, processnames, primarydepartment, processgroup)
            VALUES
                (@WorkerCode, @PrimaryStock, @Stocks, @ProcessTag, @ProcessNames, @PrimaryDepartment, @ProcessGroup)
            """;
        var rows = await connection.ExecuteAsync(new CommandDefinition(insertSql, worker, cancellationToken: ct));
        return rows > 0
            ? (true, "Worker created successfully.")
            : (false, "No worker was created.");
    }

    public async Task<(bool success, string message)> UpdateAsync(ClientInfo client, WorkerProperty worker, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(worker.WorkerCode))
            return (false, "Worker code is required.");

        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            UPDATE workerproperty SET
                primarystock = @PrimaryStock,
                stocks = @Stocks,
                processtag = @ProcessTag,
                processnames = @ProcessNames,
                primarydepartment = @PrimaryDepartment,
                processgroup = @ProcessGroup
            WHERE workercode = @WorkerCode
            """;
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, worker, cancellationToken: ct));
        return rows > 0
            ? (true, "Worker updated successfully.")
            : (false, $"Worker '{worker.WorkerCode}' was not found.");
    }

    public async Task<(bool success, string message)> DeleteAsync(ClientInfo client, string workerCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workerCode))
            return (false, "Worker code is required.");

        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = "DELETE FROM workerproperty WHERE workercode = @workerCode";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { workerCode }, cancellationToken: ct));
        return rows > 0
            ? (true, "Worker deleted successfully.")
            : (false, $"Worker '{workerCode}' was not found.");
    }

    public (bool ok, string? error) ValidateStocksCsv(string? stocks)
    {
        // TODO: validate stocks CSV against allowed stock codes for the client.
        return (true, null);
    }

    public (bool ok, string? error) ValidateProcessNamesCsv(string? processNames)
    {
        // TODO: validate processnames CSV against allowed process names for the client.
        return (true, null);
    }

    public async Task<IEnumerable<SelectOption>> GetStockOptionsAsync(ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT [code] AS [Value], [name] AS [Label]
            FROM dcstock
            WHERE [sessionid] = dbo.GetDCObjectSessionid('stock', null)
            ORDER BY [name] ASC
            """;
        return await connection.QueryAsync<SelectOption>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IEnumerable<SelectOption>> GetProcessNameOptionsAsync(ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT [NAME] AS [Value], [DISPLAYLABEL] AS [Label]
            FROM processtypes
            ORDER BY [DISPLAYLABEL] ASC
            """;
        return await connection.QueryAsync<SelectOption>(new CommandDefinition(sql, cancellationToken: ct));
    }
}
