using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class WikiService(SqlConnectionProvider sqlConnectionProvider) : IWikiService
{
    private const string TableName = "settingswiki";

    private const string SelectColumns = """
        id AS Id,
        title AS Title,
        content AS Content,
        lastupdated AS LastUpdated
        """;

    private const string EnsureTableSql = $"""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{TableName}')
        BEGIN
            CREATE TABLE [dbo].[{TableName}] (
                [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [title] NVARCHAR(255) NOT NULL,
                [content] NVARCHAR(MAX) NULL,
                [lastupdated] DATETIME NOT NULL
            )
        END
        """;

    private bool _tableEnsured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    private async Task EnsureTableAsync(SqlConnection connection, CancellationToken ct)
    {
        if (_tableEnsured)
            return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_tableEnsured)
                return;
            await connection.ExecuteAsync(new CommandDefinition(EnsureTableSql, cancellationToken: ct));
            _tableEnsured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public async Task<IEnumerable<WikiPage>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);
        var sql = $"SELECT {SelectColumns} FROM {TableName} ORDER BY title ASC";
        return await connection.QueryAsync<WikiPage>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<WikiPage?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);
        var sql = $"SELECT {SelectColumns} FROM {TableName} WHERE id = @id";
        return await connection.QuerySingleOrDefaultAsync<WikiPage>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<(bool success, string message, int id)> CreateAsync(WikiPage page, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(page.Title))
            return (false, "Title is required.", 0);

        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);

        var sql = $"""
            INSERT INTO {TableName} (title, content, lastupdated)
            OUTPUT INSERTED.id
            VALUES (@Title, @Content, @LastUpdated)
            """;
        page.LastUpdated = DateTime.UtcNow;
        var newId = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, page, cancellationToken: ct));

        return newId > 0
            ? (true, "Wiki page created.", newId)
            : (false, "No wiki page was created.", 0);
    }

    public async Task<(bool success, string message)> UpdateAsync(WikiPage page, CancellationToken ct = default)
    {
        if (page.Id <= 0)
            return (false, "Page id is required.");
        if (string.IsNullOrWhiteSpace(page.Title))
            return (false, "Title is required.");

        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);

        var sql = $"""
            UPDATE {TableName} SET
                title = @Title,
                content = @Content,
                lastupdated = @LastUpdated
            WHERE id = @Id
            """;
        page.LastUpdated = DateTime.UtcNow;
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, page, cancellationToken: ct));
        return rows > 0
            ? (true, "Wiki page updated.")
            : (false, $"Wiki page '{page.Id}' was not found.");
    }

    public async Task<(bool success, string message)> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (id <= 0)
            return (false, "Page id is required.");

        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);

        var sql = $"DELETE FROM {TableName} WHERE id = @id";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return rows > 0
            ? (true, "Wiki page deleted.")
            : (false, $"Wiki page '{id}' was not found.");
    }
}
