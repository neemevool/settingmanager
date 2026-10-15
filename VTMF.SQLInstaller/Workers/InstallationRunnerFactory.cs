using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Installers;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Workers;

public class InstallationRunnerFactory
{
    private readonly IServiceProvider _rootProvider;

    

    public InstallationRunnerFactory(IServiceProvider rootProvider)
    {
        _rootProvider = rootProvider;
    }

    public async Task<bool> RunInstallationAsync(InstallConfig config, CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();

        // Register the specific config for this run
        services.AddSingleton(config);

        // Reuse shared services from root provider
        services.AddSingleton(_rootProvider.GetRequiredService<IKeyReceiverService>());
        services.AddSingleton(_rootProvider.GetRequiredService<ILoggerFactory>());
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Register all services that depend on InstallConfig
        services.AddSingleton<DatabaseService>(sp =>
            new DatabaseService(
                config.ConnectionString,
                sp.GetRequiredService<ILogger<DatabaseService>>(),
                config));
        services.AddSingleton<DatabaseInstaller>();
        services.AddSingleton<DatabaseCreator>();
        services.AddSingleton<SqlScriptParser>();
        services.AddSingleton<XmlInstallParser>();
        services.AddSingleton<FileInstaller>();
        services.AddSingleton<AdminInstaller>();
        services.AddSingleton<ServiceInstaller>();
        services.AddSingleton<ServiceAccountInstaller>();
        services.AddSingleton<SettingsInstaller>();
        services.AddSingleton<IInstallerFactory, InstallerFactory>();
        services.AddSingleton<InstallationOrchestrator>();

        

        await using var scopedProvider = services.BuildServiceProvider();
        var orchestrator = scopedProvider.GetRequiredService<InstallationOrchestrator>();
        var keyReceiverService = scopedProvider.GetRequiredService<IKeyReceiverService>();
        var logger = scopedProvider.GetRequiredService<ILogger<InstallationRunnerFactory>>();

        try
        {
            logger.LogInformation("VTMF SQL Installer starting...");

            keyReceiverService.LogToKeyReceiver(new InstallLogs
            {
                ClientName = config.ClientName,
                LogMessage = "VTMF SQL Installer started",
                LogLevel = "Information"
            });

            var success = await orchestrator.RunAsync();

            keyReceiverService.LogToKeyReceiver(new InstallLogs
            {
                ClientName = config.ClientName,
                LogMessage = "VTMF SQL Installer completed",
                LogLevel = success ? "Information" : "Error"
            });

            if (success)
                logger.LogInformation("VTMF SQL Installer completed successfully");
            else
                logger.LogError("VTMF SQL Installer completed with errors");

            return success;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "VTMF SQL Installer crashed with unhandled exception");
            throw;
        }
    }
}

