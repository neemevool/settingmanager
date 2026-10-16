using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Services;
using VTMF.SQLInstaller.Services;

namespace VTMF.Settings.Tests.Sql;

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ClientName = "test";
    public const string EmptyClientName = "empty";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
        .Build();

    public SqlConnectionProvider Connections { get; private set; } = null!;

    public SmClient Sm => new(Connections);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var master = _container.GetConnectionString();

        // Client databases may still run at an older compatibility level after the server upgrade.
        await ExecuteAsync(master, "CREATE DATABASE smtest; ALTER DATABASE smtest SET COMPATIBILITY_LEVEL = 150; CREATE DATABASE smempty;");

        var builder = new SqlConnectionStringBuilder(master);
        Connections = new SqlConnectionProvider(new Dictionary<string, SqlConnectionParameters>
        {
            [ClientName] = Parameters(builder, "smtest", ClientName),
            [EmptyClientName] = Parameters(builder, "smempty", EmptyClientName),
        });

        var test = Connections.GetConnectionString(ClientName);
        foreach (var script in Database5Scripts.BaseScripts.Concat(Database5Scripts.SettingsManagementScripts))
            await RunScriptFileAsync(test, script);
        await RunScriptFileAsync(test, Path.Combine(AppContext.BaseDirectory, "TestData", "seed.sql"));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public Task ExecuteOnTestAsync(string sql) => ExecuteAsync(Connections.GetConnectionString(ClientName), sql);

    private static SqlConnectionParameters Parameters(SqlConnectionStringBuilder master, string database, string client) => new()
    {
        ClientName = client,
        Server = $"Server={master.DataSource};Database={database};TrustServerCertificate=True",
        UserId = master.UserID,
        Password = master.Password,
    };

    private static async Task RunScriptFileAsync(string connectionString, string path)
    {
        var text = await File.ReadAllTextAsync(path); // detects UTF-16 BOMs used by some database5 files
        var parser = new SqlScriptParser(NullLogger<SqlScriptParser>.Instance);
        foreach (var batch in parser.SplitIntoBatches(text))
        {
            try
            {
                await ExecuteAsync(connectionString, batch);
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"Script {path} failed: {ex.Message}", ex);
            }
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
