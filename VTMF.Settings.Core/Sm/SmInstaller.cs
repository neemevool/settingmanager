using VTMF.Settings.Services;
using VTMF.SQLInstaller.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>Installs or upgrades the sm module in a client database from the shipped scripts, in one transaction.</summary>
public sealed class SmInstaller(SqlConnectionProvider connections, SmScriptSet scripts)
{
    public async Task<SmInstallResult> InstallAsync(string client, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        if (string.Equals(client, SqlConnectionParameters.DefaultMetaBaseClientName, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"'{client}' is not an install target.");

        await using var connection = connections.GetConnection(client);
        return await SmModuleInstall.RunAsync(connection, client, scripts, cancellationToken).ConfigureAwait(false);
    }
}
