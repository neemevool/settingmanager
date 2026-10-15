using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace VTMF.SQLInstaller.Services;

/// <summary>
/// Lightweight class for testing database server connectivity at startup.
/// </summary>
public class DatabaseConnectionTester
{
    private readonly ILogger<DatabaseConnectionTester> _logger;

    public DatabaseConnectionTester(ILogger<DatabaseConnectionTester> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tests if the database server is reachable.
    /// </summary>
    public async Task<bool> TestServerConnectionAsync(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
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
            _logger.LogError(ex, "Database server connection failed: {Message}", ex.Message);
            return false;
        }
    }
}
