using Microsoft.Extensions.Logging;
using System.Text;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Installers;

namespace VTMF.SQLInstaller.Services;

public class InstallationOrchestrator
{
    private readonly InstallConfig _config;
    private readonly XmlInstallParser _xmlParser;
    private readonly DatabaseInstaller _databaseInstaller;
    private readonly DatabaseCreator _databaseCreator;
    private readonly IInstallerFactory _installerFactory;
    private readonly ILogger<InstallationOrchestrator> _logger;
    private readonly IKeyReceiverService _keyReceiverService;

    public InstallationOrchestrator(
        InstallConfig config,
        XmlInstallParser xmlParser,
        DatabaseInstaller databaseInstaller,
        DatabaseCreator databaseCreator,
        IInstallerFactory installerFactory,
        ILogger<InstallationOrchestrator> logger,
        IKeyReceiverService keyReceiverService)
    {
        _config = config;
        _xmlParser = xmlParser;
        _databaseInstaller = databaseInstaller;
        _databaseCreator = databaseCreator;
        _installerFactory = installerFactory;
        _logger = logger;
        _keyReceiverService = keyReceiverService;
    }

    public async Task<bool> RunAsync()
    {
        try
        {
            _logger.LogInformation("=== SQL Installer Starting ===");
            _logger.LogInformation("Scripts Directory: {ScriptsDir}", _config.SqlScriptsDir);

            var canInstall = await _databaseInstaller.CanConnect();
            if (!canInstall)
            {
                _logger.LogError("Database connection check failed. Aborting installation.");
                return false;
            }

            // Check if database exists, create it if needed
            var databaseExists = await _databaseInstaller.DatabaseIsCreated();
            if (!databaseExists)
            {
                _logger.LogInformation("Database does not exist. Attempting to create: {DatabaseName}", _config.DatabaseName);
                try
                {
                    await _databaseCreator.CreateDatabaseAsync(_config.DatabaseName);
                    _logger.LogInformation("Database created successfully: {DatabaseName}", _config.DatabaseName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create database: {DatabaseName}", _config.DatabaseName);
                    return false;
                }
            }
            else
            {
                _logger.LogInformation("Database already exists: {DatabaseName}", _config.DatabaseName);
            }

            // Parse install.xml
            var installXmlPath = Path.Combine(_config.InstallXmlPath);
            if (!File.Exists(installXmlPath))
            {
                _logger.LogError("Install configuration file not found: {InstallXmlPath}", installXmlPath);
                return false;
            }

            var install = XmlInstallParser.Parse(installXmlPath);
            _logger.LogInformation("Loaded {SetCount} installation sets", install.Sets.Count);

            await _databaseInstaller.InitializeVersionCache();
            // Process each set in order
            var totalInstalled = 0;
            var totalSkipped = 0;

            List<string> sets = _config.Sets?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? new List<string>();

            foreach (var set in install.Sets)
            {
                if (set.IsOptional && ! sets.Contains(set.Name))
                {
                    _logger.LogInformation("--- Skipping Set: {SetName} because optional ---", set.Name);
                    continue;
                }

                _logger.LogInformation("--- Processing Set: {SetName} ({ItemCount} items) ---",
                    set.Name, set.Items.Count);

                foreach (var item in set.Items)
                {
                    try
                    {
                        IInstaller installer = _installerFactory.GetInstaller(item.Type);

                        var installedItems = await installer.InstallAsync(item, set.Name);
                        var count = installedItems.Count();

                        if (count > 0)
                        {
                            totalInstalled += count;
                            _logger.LogInformation("✓ Installed {Count} item(s) for: {ItemName}", count, item.ItemName);
                        }
                        else
                        {
                            totalSkipped++;
                            _logger.LogInformation("○ Skipped (already installed): {ItemName}", item.ItemName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "✗ Failed to install item: {ItemName}", item.ItemName);
                        StringBuilder errormessages = new StringBuilder();
                        errormessages.AppendLine($"Installation failed for item '{item.ItemName}' in set '{set.Name}': {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            errormessages.AppendLine($"Inner Exception: {ex.InnerException.Message}");
                        }
                        _keyReceiverService.LogToKeyReceiver(new InstallLogs
                        {
                            ClientName = _config.ClientName,
                            LogMessage = errormessages.ToString(),
                            LogLevel = "Error"
                        });
                        throw new InvalidOperationException($"Installation failed for item '{item.ItemName}' in set '{set.Name}'", ex);
                    }
                }

                _logger.LogInformation("--- Completed Set: {SetName} ---", set.Name);
            }

            _logger.LogInformation("=== SQL Installer Completed Successfully ===");
            _logger.LogInformation("Total Installed: {Installed}, Total Skipped: {Skipped}",
             totalInstalled, totalSkipped);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== SQL Installer Failed ===");
            return false;
        }
    }
}
