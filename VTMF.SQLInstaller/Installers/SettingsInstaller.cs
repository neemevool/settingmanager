using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VTMF.SQLInstaller.Installers;
public class SettingsInstaller : IInstaller
{
    private readonly DatabaseService _databaseService;
    private readonly InstallConfig _config;
    private readonly ILogger<DatabaseInstaller> _logger;

    public SettingsInstaller(
        DatabaseService databaseService,
        InstallConfig config,
        ILogger<DatabaseInstaller> logger)
    {
        _databaseService = databaseService;
        _config = config;
        _logger = logger;
    }

    public async Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName)
    {
        _logger.LogInformation("Processing settings installation: {ItemName} (check: {Check}, isFolder: {IsFolder})",
        installItem.ItemName, installItem.Check, installItem.IsFolder);
        //<item name="basesettings" check="database" isfolder="true" type="settings" source="$/Sawmill/database5/basesettings/Settings.config"/>
        var installedItems = new List<InstalledItem>();

        var settingFile = FileUtils.GetFullPath(installItem, _config);
        if (!File.Exists(settingFile)) {
            _logger.LogWarning("Settings file not found: {SettingFile}", settingFile);
            return installedItems;
        }

        InstallItem fileInstallItem = installItem with
        {
            FullPath = settingFile,
            RelativePath = FileUtils.GetRelativePath(installItem, _config),
            Check = "file"
        };

        var installedItem = await InstallSettingFileAsync(fileInstallItem, setName);

        return installedItems;
    }

    private async Task<InstalledItem?> InstallSettingFileAsync(InstallItem installItem, string setName)
    {
        var installedVersion = await _databaseService.CheckInstallAsync(installItem);
        if (installedVersion>0)
        {
            return null;
        }
        var scriptPath = installItem.FullPath;
        var relativePath = installItem.RelativePath;

        // read the UTF-8 encoded settings XML file
        _logger.LogInformation("Reading settings file: {FullPath}", scriptPath);
        var settingsXml = await File.ReadAllTextAsync(scriptPath, Encoding.UTF8);

        // Parse and use OuterXml to avoid encoding declaration conflicts
        var doc = XDocument.Parse(settingsXml);
        var configuration = doc.Root;
        if (configuration == null)
        {
            _logger.LogWarning("Settings file is empty or invalid: {FullPath}", scriptPath);
            return null;
        }
        var appSettingsElements = configuration
            .Element("appSettings")
            ?.Elements("add")
            ?? Enumerable.Empty<XElement>();

        foreach (var setting in appSettingsElements)
        {
            var settingXml = setting.ToString();
            await _databaseService.SaveSettings(settingXml, "ALL");
        }

        

        return new InstalledItem
        {
            ItemName = installItem.ItemName,
            ItemType = installItem.Type,
            InstallTime = DateTime.UtcNow,
            Source = scriptPath,
            Target = null,
            SetName = setName,
            Version = 1
        };
    }
}
