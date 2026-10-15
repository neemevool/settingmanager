using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Installers;
public class ServiceAccountInstaller(ILogger<AdminInstaller> _logger, InstallConfig _config, DatabaseService _databaseService, IKeyReceiverService _keyReceiverService) : IInstaller
{
    public async Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName)
    {
        
        var emptyResult = Array.Empty<InstalledItem>();
        if (string.IsNullOrWhiteSpace(_config.Admins))
        {
            _logger.LogError("No admin users. Cannot create service accounts");
            return emptyResult;
        }
        var installedItems = new List<InstalledItem>();
        var admins = EncryptUtils.ParseAdmins(_config.Admins);
        if (!admins.Any())
        {
            _logger.LogError("No valid admin users parsed. Cannot create service accounts");
            return emptyResult;
        }
        var firstAdmin = admins.First();
        var idcode = firstAdmin.IdCode;
        var username = installItem.ItemName;
        var databaseName = _config.DatabaseName ?? "master";
        
        var installed = await _databaseService.CheckInstallAsync(installItem);
        if (installed > 0)
        {
            _logger.LogInformation("Service account {Username} already installed. Skipping.", username);
            return emptyResult;
        }
        var exists = await _databaseService.CheckServerUserExists(username);
        if (!exists)
        {
            _logger.LogInformation("Creating serviceaccount user: {Username}", username);
            
            var password = EncryptUtils.GenerateRandomPassword(32);
            var passwordFile = EncryptUtils.RunShellEncryptorJar(idcode, username, password);
            var scripts = GenerateServiceAccountCreationScript(databaseName, username, password);
            foreach (var (sql, dbName) in scripts)
            {
                try
                {
                    await _databaseService.ExecuteDatabaseCreationScriptAsync(sql, dbName);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Batch failed: {sql}, database {dbName}: {ex.Message}");
                }
            }
            var sqlSecretName = System.IO.Path.GetFileNameWithoutExtension(passwordFile);
            await _keyReceiverService.SendSqlSecret(new SqlSecret(

                _config.ClientName,
                EncryptUtils.ReadEncryptedFile(passwordFile),
                idcode, sqlSecretName
            ));
        }

        var databaseUserExists = await _databaseService.CheckDatabaseUserExists(databaseName, username);
        if (!databaseUserExists)
        {
            _logger.LogInformation("Creating database user for serviceaccount: {Username} in database {DatabaseName}", username, databaseName);
            var (createUserSql, dbName) = GetDatabaseUserCreationCommand(username, databaseName);
            await _databaseService.ExecuteDatabaseCreationScriptAsync(createUserSql, databaseName);

            foreach (var installSpec in installItem.Spec)
            {
                if (installSpec.Name == "role")
                {
                    var (roleAssignSql, roleDbName) = GetRoleAssignmentCommand(installSpec.Value, username, databaseName);
                    _logger.LogInformation("Assigning role {RoleName} to serviceaccount: {Username} in database {DatabaseName}", installSpec.Value, username, databaseName);
                    await _databaseService.ExecuteDatabaseCreationScriptAsync(roleAssignSql, databaseName);
                }
            }
        }
        return emptyResult;
    }

    private static IEnumerable<(string sql, string databaseName)> GenerateServiceAccountCreationScript(string databaseName, string username, string password)
    {
        var escapedDatabaseName = DatabaseUtils.EscapeSqlString(databaseName);
        yield return ($"""
            CREATE LOGIN [{username}] WITH PASSWORD=N'{password}', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=ON
            """, "master");

        yield return ($"""
            CREATE USER [{username}] FOR LOGIN [{username}]
            """, "msdb");

        yield return ($"""
            CREATE USER [{username}] FOR LOGIN [{username}]
            """, "tempdb");

       
        
    }

    private static (string sql, string databaseName) GetDatabaseUserCreationCommand(string username, string databaseName)
    {
        var escapedDatabaseName = DatabaseUtils.EscapeSqlString(databaseName);
        return ($"""
            CREATE USER [{username}] FOR LOGIN [{username}]
            """, escapedDatabaseName);
    }

    private static (string sql, string databaseName) GetRoleAssignmentCommand(string roleName, string username, string databaseName)
    {
        var escapedDatabaseName = DatabaseUtils.EscapeSqlString(databaseName);
        return ($"""
            ALTER ROLE [{roleName}] ADD MEMBER [{username}]
            """, escapedDatabaseName);
    }
}
    


