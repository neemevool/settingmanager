using Microsoft.Data.SqlClient;
using VTMF.SQLInstaller.Entities;

namespace VTMF.Settings.Services;

public class SqlConnectionProvider
{
    private readonly Dictionary<string, SqlConnectionParameters> _connectionParameters;

    public SqlConnectionProvider(Dictionary<string, SqlConnectionParameters> connectionParameters)
    {
        _connectionParameters = connectionParameters;
    }

    public SqlConnection GetConnection(string clientName)
    {
        if (!_connectionParameters.TryGetValue(clientName, out var parameters))
        {
            throw new KeyNotFoundException($"No SQL connection parameters found for client '{clientName}'.");
        }

        return new SqlConnection(parameters.ConnectionString);
    }
    public string GetConnectionString(string clientName)
    {
        if (!_connectionParameters.TryGetValue(clientName, out var parameters))
        {
            throw new KeyNotFoundException($"No SQL connection parameters found for client '{clientName}'.");
        }
        return parameters.ConnectionString;
    }

    public SqlConnection GetDefaultMetaBaseConnection()
    {
        return GetConnection(SqlConnectionParameters.DefaultMetaBaseClientName);
    }
    public SqlConnection GetMetaBaseConnection(string clientName)
    {
        var clientMetaBaseName = _connectionParameters[clientName].MetaBaseName;
        return GetConnection(clientMetaBaseName);
    }

    public IEnumerable<string> GetAvailableClients()
    {
        return _connectionParameters.Keys.Where(k => k!=SqlConnectionParameters.DefaultMetaBaseClientName);
    }

    public InstallConfig GetInstallConfig(string clientName)
    {
        if (!_connectionParameters.TryGetValue(clientName, out var clientParams))
        {
            throw new KeyNotFoundException($"No SQL connection parameters found for client '{clientName}'.");
        }

        _connectionParameters.TryGetValue(SqlConnectionParameters.DefaultMetaBaseClientName, out var defaults);

        var installXmlPath = GetInstallXmlPath(clientName)
            ?? throw new InvalidOperationException($"InstallXmlPath is not configured for client '{clientName}'.");

        var databaseName = clientParams.DatabaseName
            ?? defaults?.DatabaseName
            ?? throw new InvalidOperationException($"DatabaseName could not be determined for client '{clientName}'.");

        return new InstallConfig
        {
            ClientName = clientName,
            ConnectionString = clientParams.ConnectionString,
            InstallXmlPath = installXmlPath,
            DatabaseName = databaseName,
            SqlScriptsDir = ResolvePath(clientParams.SqlScriptsDir ?? defaults?.SqlScriptsDir ?? ""),
            InstallMode = clientParams.InstallMode ?? defaults?.InstallMode ?? "install",
            SqlScriptsPrefix = clientParams.SqlScriptsPrefix ?? defaults?.SqlScriptsPrefix ?? "",
            ConfigDir = ResolvePath(clientParams.ConfigDir ?? defaults?.ConfigDir ?? "")+"-"+clientName,
            KeyReceiverUrl = clientParams.KeyReceiverUrl ?? defaults?.KeyReceiverUrl,
            KeyReceiverToken = clientParams.KeyReceiverToken ?? defaults?.KeyReceiverToken,
            Admins = clientParams.Admins ?? defaults?.Admins,
            TfsToken = clientParams.TfsToken ?? defaults?.TfsToken
        };
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            return path;
        return Path.Combine(Directory.GetCurrentDirectory(), path);
    }

    public string? GetInstallXmlPath(string clientName)
    {
        if (!_connectionParameters.TryGetValue(clientName, out var parameters))
        {
            throw new KeyNotFoundException($"No SQL connection parameters found for client '{clientName}'.");
        }
        //  determine if the path is absolute, then return as is, otherwise return working directory + path

        if (string.IsNullOrEmpty(parameters.InstallXmlPath))
        {
            return null;
        }

        // Return absolute paths as-is, otherwise combine with current working directory
        if (Path.IsPathRooted(parameters.InstallXmlPath))
        {
            return parameters.InstallXmlPath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), parameters.InstallXmlPath);
    }
}
