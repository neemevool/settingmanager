using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;
    private readonly bool _dryrun;
    private readonly InstallConfig _config;

    public DatabaseService(string connectionString, ILogger<DatabaseService> logger, InstallConfig config)
    {
        _connectionString = connectionString;
        _logger = logger;
        _config = config;
        _dryrun = config.InstallMode != "install";
    }

    /// <summary>
    /// Checks if an item is installed in the database and returns its version.
    /// Returns null if the item is not installed.
    /// </summary>
    public async Task<int?> CheckInstallAsync(InstallItem item)
    {
        try
        {
            if (item.Check.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var historyFilePath = Path.Combine(_config.ConfigDir, "installhistory",
                   item.RelativePath.Replace("/", "_"));
                return File.Exists(historyFilePath) ? 10000000 : (int?)null;
            }
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogInformation("Checking installation status for item: {Source}", item.Source);
            await using var command = new SqlCommand("dbo.SetupCheckInstall", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            command.Parameters.AddWithValue("@itemname", item.Source);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : (int?)result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking installation status for item: {Source}", item.Source);
            throw;
        }
    }

    /// <summary>
    /// Registers an installation in the database.
    /// </summary>
    public async Task RegisterInstallationAsync(
        InstallItem item, string setName, int version)
    {
        if (_dryrun) return;

        if (item.Check.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            // Create history file
            var historyDir = Path.Combine(_config.ConfigDir, "installhistory");
            Directory.CreateDirectory(historyDir);
            var historyFile = Path.Combine(historyDir, item.RelativePath.Replace("/", "_"));
            await File.WriteAllTextAsync(historyFile, $"Installed at: {DateTime.UtcNow:O}");
            _logger.LogDebug("Created history file: {HistoryFile}", historyFile);
            return;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("dbo.SetupRegisterInstallation", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            command.Parameters.AddWithValue("@itemname", item.Source); // backward compatibility with Powershell installer
            command.Parameters.AddWithValue("@itemtype", item.Type);
            command.Parameters.AddWithValue("@version", version);
            command.Parameters.AddWithValue("@source", item.Source ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@target", (object)DBNull.Value);
            command.Parameters.AddWithValue("@setname", $"{item.ItemName}:{setName}");

            await command.ExecuteNonQueryAsync();

            _logger.LogInformation("Registered installation: {ItemName} v{Version}", item.ItemName, version);
        }
        catch (Exception ex)
        {
            
            _logger.LogError(ex, "Error registering installation for item: {ItemName}", item.ItemName);
            throw;
        }
    }

    public async Task SaveSettings(string settingsXml, string workercode)
    {
        if (string.IsNullOrWhiteSpace(settingsXml))
        {
            return;
        }
        if (_dryrun)
        {
            return;
        }
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("dbo.SaveSetting", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            command.Parameters.AddWithValue("@settingnode", settingsXml);
            command.Parameters.AddWithValue("@workercode", workercode);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Error saving settings XML. Connection string: {ConnectionString}", loggableConnectionString);
            throw;
        }
    }

    /// <summary>
    /// Executes a SQL script batch.
    /// </summary>
    public async Task ExecuteSqlBatchAsync(string sqlBatch)
    {
        if (string.IsNullOrWhiteSpace(sqlBatch))
        {
            return;
        }

        if (_dryrun)
        {
            return;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sqlBatch, connection)
            {
                CommandTimeout = 300 // 5 minute timeout
            };
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Error executing SQL batch. Batch length: {Length}, Connection string: {ConnectionString}", sqlBatch.Length, loggableConnectionString);
            throw;
        }
    }

    /// <summary>
    /// Tests the database connection.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        if (_dryrun)
        {
            _logger.LogInformation("Dry run mode - skipping database connection test");
            return true;
        }
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            var cancellationTimeout = TimeSpan.FromSeconds(5);

            var cancellationTokenSource = new CancellationTokenSource(cancellationTimeout);
            await connection.OpenAsync(cancellationTokenSource.Token);
            _logger.LogInformation("Database connection successful");
            return true;
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Database connection failed. Message: {Message}. Connection string: {ConnectionString}", ex.Message, loggableConnectionString);
            return false;
        }
    }

    public async Task<bool> TestServerConnectionAsync()
    {
        // test only server connection without database
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = "master"
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.OpenAsync(cancellationTokenSource.Token);
            _logger.LogInformation("Database server connection successful");
            return true;
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Database server connection failed. Message: {Message}. Connection string: {ConnectionString}", ex.Message, loggableConnectionString);
            return false;
        }
    }

    public async Task<bool> CheckDatabaseExists()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            var databaseName = builder.InitialCatalog;
            // Connect to master database to check for existence of the target database
            builder.InitialCatalog = "master";
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(
                "SELECT COUNT(*) FROM sys.databases WHERE name = @DatabaseName",
                connection);
            command.Parameters.AddWithValue("@DatabaseName", databaseName ?? string.Empty);

            var count = (int?)await command.ExecuteScalarAsync() ?? 0;
            return count > 0;
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Error checking existence of database. Message: {Message}. Connection string: {ConnectionString}", ex.Message, loggableConnectionString);
            throw;
        }
    }

    /// <summary>
    /// Executes a database creation script in the master database context.
    /// This method temporarily switches the connection to the master database to execute the script.
    /// </summary>
    public async Task ExecuteDatabaseCreationScriptAsync(string sqlBatch, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(sqlBatch))
        {
            return;
        }

        if (_dryrun)
        {
            _logger.LogInformation("Dry run mode - skipping database creation script");
            return;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sqlBatch, connection)
            {
                CommandTimeout = 300 // 5 minute timeout
            };
            await command.ExecuteNonQueryAsync();

            _logger.LogInformation("Database script executed successfully in {databaseName}", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing database creation script. Batch length: {Length}", sqlBatch.Length);
            throw;
        }
    }

    public async Task<bool> CheckServerUserExists(string username)
    {
        try
        {

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT COUNT(*) FROM sys.server_principals WHERE name = @Username",
                connection);
            command.Parameters.AddWithValue("@Username", username);
            var count = (int?)await command.ExecuteScalarAsync() ?? 0;
            return count > 0;
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Error checking existence of user. Message: {Message}. Connection string: {ConnectionString}", ex.Message, loggableConnectionString);
            throw;
        }
    }

    public async Task<bool> CheckDatabaseUserExists(string databaseName, string username)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = databaseName
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT COUNT(*) FROM sys.database_principals WHERE name = @Username",
                connection);
            command.Parameters.AddWithValue("@Username", username);
            var count = (int?)await command.ExecuteScalarAsync() ?? 0;
            return count > 0;
        }
        catch (Exception ex)
        {
            var loggableConnectionString = LogConnectionString();
            _logger.LogError(ex, "Error checking existence of user. Message: {Message}. Connection string: {ConnectionString}", ex.Message, loggableConnectionString);
            throw;
        }
    }


    private string LogConnectionString()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                Password = "****"
            };
            return builder.ConnectionString;
        }
        catch
        {
            return "Invalid connection string";
        }
    }
}
