using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Installers;
public class AdminInstaller(ILogger<AdminInstaller> _logger, InstallConfig _config, DatabaseService _databaseService, IKeyReceiverService _keyReceiverService) : IInstaller
{
    public async Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName)
    {
        if (string.IsNullOrWhiteSpace(_config.Admins))
        {
            _logger.LogInformation("No admin users to create.");
            return Array.Empty<InstalledItem>();
        }
        var installedItems = new List<InstalledItem>();
        var admins = EncryptUtils.ParseAdmins(_config.Admins);
        foreach (var admin in admins)
        {
            var idcode = admin.IdCode;
            var username = admin.UserName;

            var password = EncryptUtils.GenerateRandomPassword();
            var databaseName = _config.DatabaseName ?? "master";
            var exists = await _databaseService.CheckServerUserExists(username);
            if (exists) {
                _logger.LogInformation("Admin user already exists: {Username}", username);
                continue;
            }
            _logger.LogInformation("Creating admin user: {Username}", username);
            var passwordFile = EncryptUtils.RunShellEncryptorJar(idcode, username, password);
            if (passwordFile == null)
            {
                _logger.LogError($"Cannot create password for {username}");
                _keyReceiverService.LogToKeyReceiver(new InstallLogs
                {
                    ClientName = _config.ClientName,
                    LogLevel = "Error",
                    LogMessage = $"Admin user {username} password creation failed"
                });
                continue;
            }
            var sqlCommands = GenerateUserCreationScript(databaseName, username, password).ToList();
            foreach (var (sql, dbName) in sqlCommands)
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
            installedItems.Add(new InstalledItem
            {
                ID = 0,
                ItemName = installItem.ItemName,
                ItemType = installItem.Type,
                InstallTime = DateTime.UtcNow,
                Source = installItem.Source,
                Target = null,
                SetName = setName,
                Version = 0
            });
            _logger.LogInformation("Admin user created: {Username}", username);
        }
        return installedItems.AsReadOnly();
    }

    private static IEnumerable<(string sql, string databaseName)> GenerateUserCreationScript(string databaseName,string username, string password)
    {
        var escapedDatabaseName = DatabaseUtils.EscapeSqlString(databaseName);
        yield return ($"""
            CREATE LOGIN [{username}] WITH PASSWORD=N'{password}', DEFAULT_DATABASE=[master], CHECK_EXPIRATION=OFF, CHECK_POLICY=ON
            """, "master");

        yield return ($"""
            ALTER SERVER ROLE [sysadmin] ADD MEMBER [{username}]
            """, "master");

        yield return ($"""
            CREATE USER [{username}] FOR LOGIN [{username}]
            """, "msdb");

        yield return ($"""
            CREATE USER [{username}] FOR LOGIN [{username}]
            """, "tempdb");
        
    }

    
}