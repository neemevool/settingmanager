using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VTMF.SQLInstaller.Installers;

namespace VTMF.SQLInstaller.Services;
public class InstallerFactory : IInstallerFactory
{
    private readonly IServiceProvider _serviceProvider;

    public InstallerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IInstaller GetInstaller(string itemType)
    {
        return itemType.ToLowerInvariant() switch
        {
            "database" => _serviceProvider.GetRequiredService<DatabaseInstaller>(),
            "file" => _serviceProvider.GetRequiredService<FileInstaller>(),
            "settings" => _serviceProvider.GetRequiredService<SettingsInstaller>(),
            "admin" => _serviceProvider.GetRequiredService<AdminInstaller>(),
            "service" => _serviceProvider.GetRequiredService<ServiceInstaller>(),
            "serviceaccount" => _serviceProvider.GetRequiredService<ServiceAccountInstaller>(),
            _ => throw new InvalidOperationException($"Unknown item type: {itemType}")
        };
    }
}
