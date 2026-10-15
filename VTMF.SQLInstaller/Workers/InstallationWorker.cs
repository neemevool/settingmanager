using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Workers;

public class InstallationWorker(
        InstallationRunnerFactory runner,
        InstallConfig config,
        IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    
    
    

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var success = await runner.RunInstallationAsync(config, stoppingToken);
            await Task.Delay(3000, stoppingToken); // Allow async logs to flush
            Environment.ExitCode = success ? 0 : 1;
        }
        catch (Exception)
        {
            
            Environment.ExitCode = 1;
        }
        finally
        {
            // Signal the application to stop
            applicationLifetime.StopApplication();
        }
    }
}
