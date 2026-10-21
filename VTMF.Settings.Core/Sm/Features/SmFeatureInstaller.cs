using Microsoft.Data.SqlClient;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm.Features;

/// <summary>DI wrapper so the client and metabase script sets are distinct singletons.</summary>
public sealed record SmMetabaseScripts(SmScriptSet Set);

/// <summary>Installs or upgrades the settingsmetabase module (feature definitions) in the shared metadata database, in one transaction.</summary>
public sealed class SmFeatureInstaller(
    SqlConnectionProvider connections,
    SmMetabaseScripts scripts,
    string connectionName = SqlConnectionParameters.DefaultMetaBaseClientName)
{
    public async Task<SmInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        SqlConnection connection;
        try
        {
            connection = connections.GetConnection(connectionName);
        }
        catch (KeyNotFoundException ex)
        {
            throw new SmException(SmErrorCodes.MetabaseNotConfigured,
                "No DefaultMetaBase connection is configured; features need the shared metadata database.", ex);
        }

        await using (connection.ConfigureAwait(false))
        {
            try
            {
                return await SmModuleInstall.RunAsync(connection, connectionName, scripts.Set, cancellationToken).ConfigureAwait(false);
            }
            catch (SmException ex) when (ex.Code == SmErrorCodes.ModuleNewerThanTool)
            {
                throw new SmException(SmErrorCodes.FeatureModuleNewerThanTool, ex.Message, ex);
            }
        }
    }
}
