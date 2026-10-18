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
    public const string FreshClientName = "fresh";
    public const string UpgradeClientName = "upgrade";
    public const string ConcurrentClientName = "concurrent";
    public const string BrokenClientName = "broken";
    public const string OutdatedClientName = "outdated";
    public const string NewerClientName = "newer";
    public const string ToolFreshClientName = "toolfresh";
    public const string UnreachableClientName = "unreachable";

    public static string[] AllClientNames { get; } =
        [ClientName, EmptyClientName, FreshClientName, UpgradeClientName, ConcurrentClientName, BrokenClientName, OutdatedClientName, NewerClientName, ToolFreshClientName, UnreachableClientName];

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2019-latest")
        .Build();

    public SqlConnectionProvider Connections { get; private set; } = null!;

    public SmClient Sm => new(Connections, Scripts);

    public SmScriptSet Scripts { get; private set; } = null!;

    public SmInstaller Installer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var master = _container.GetConnectionString();

        // Level 150 is the SQL Server 2019 maximum: the supported floor.
        await ExecuteAsync(master, "CREATE DATABASE smtest; ALTER DATABASE smtest SET COMPATIBILITY_LEVEL = 150; CREATE DATABASE smempty; CREATE DATABASE smfresh; CREATE DATABASE smupgrade; CREATE DATABASE smconcurrent; CREATE DATABASE smbroken; CREATE DATABASE smoutdated; CREATE DATABASE smnewer; CREATE DATABASE smtoolfresh;");

        var builder = new SqlConnectionStringBuilder(master);
        Connections = new SqlConnectionProvider(new Dictionary<string, SqlConnectionParameters>
        {
            [ClientName] = Parameters(builder, "smtest", ClientName),
            [EmptyClientName] = Parameters(builder, "smempty", EmptyClientName),
            [FreshClientName] = Parameters(builder, "smfresh", FreshClientName),
            [UpgradeClientName] = Parameters(builder, "smupgrade", UpgradeClientName),
            [ConcurrentClientName] = Parameters(builder, "smconcurrent", ConcurrentClientName),
            [BrokenClientName] = Parameters(builder, "smbroken", BrokenClientName),
            [OutdatedClientName] = Parameters(builder, "smoutdated", OutdatedClientName),
            [NewerClientName] = Parameters(builder, "smnewer", NewerClientName),
            [ToolFreshClientName] = Parameters(builder, "smtoolfresh", ToolFreshClientName),
            [UnreachableClientName] = new SqlConnectionParameters
            {
                ClientName = UnreachableClientName,
                Server = "Server=tcp:127.0.0.1,1;Connect Timeout=2;TrustServerCertificate=True",
                UserId = "sa",
                Password = "unused",
            },
            // Not a client: lets tests prove the installer guard rather than the unknown-key path.
            [SqlConnectionParameters.DefaultMetaBaseClientName] = Parameters(builder, "smempty", SqlConnectionParameters.DefaultMetaBaseClientName),
        });

        Scripts = SmScriptSet.LoadDefault();
        Installer = new SmInstaller(Connections, Scripts);

        foreach (var client in new[] { ClientName, FreshClientName, UpgradeClientName, ConcurrentClientName, BrokenClientName, OutdatedClientName, NewerClientName, ToolFreshClientName })
        {
            foreach (var script in Database5Scripts.BaseScripts)
                await RunScriptFileAsync(Connections.GetConnectionString(client), script);
        }

        await Installer.InstallAsync(ClientName);
        await RunScriptFileAsync(Connections.GetConnectionString(ClientName), Path.Combine(AppContext.BaseDirectory, "TestData", "seed.sql"));

        await Installer.InstallAsync(UpgradeClientName);
        await ExecuteAsync(Connections.GetConnectionString(UpgradeClientName), "UPDATE sm.moduleinfo SET version = 1");

        await Installer.InstallAsync(OutdatedClientName);
        await ExecuteAsync(Connections.GetConnectionString(OutdatedClientName), "UPDATE sm.moduleinfo SET version = 1");

        await Installer.InstallAsync(NewerClientName);
        await ExecuteAsync(Connections.GetConnectionString(NewerClientName), $"UPDATE sm.moduleinfo SET version = {Scripts.Version + 1}");

        // Deterministic failure of script 030: sm.GetProcesses already exists as a table.
        var broken = Connections.GetConnectionString(BrokenClientName);
        await ExecuteAsync(broken, "EXEC(N'CREATE SCHEMA sm')");
        await ExecuteAsync(broken, "CREATE TABLE sm.GetProcesses (x int)");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public Task ExecuteOnTestAsync(string sql) => ExecuteAsync(Connections.GetConnectionString(ClientName), sql);

    public async Task<T?> ScalarOnAsync<T>(string client, string sql)
    {
        await using var connection = new SqlConnection(Connections.GetConnectionString(client));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

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
