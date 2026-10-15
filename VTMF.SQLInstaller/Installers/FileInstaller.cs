using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Installers;

public class FileInstaller : IInstaller
{
    private readonly InstallConfig _config;
    private readonly ILogger<FileInstaller> _logger;

    public FileInstaller(InstallConfig config, ILogger<FileInstaller> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName)
    {
        _logger.LogInformation("Processing file installation: {ItemName} (check: {Check})",
            installItem.ItemName, installItem.Check);

        var sourcePath = GetFullPath(installItem.Source);

        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Source file not found: {SourcePath}. Skipping.", sourcePath);
            return Enumerable.Empty<InstalledItem>();
        }

        // Check if already installed
        if (installItem.Check.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            var historyFilePath = Path.Combine(_config.ConfigDir, "installhistory", Path.GetFileName(sourcePath));
            if (File.Exists(historyFilePath))
            {
                _logger.LogInformation("File {ItemName} already installed (history file exists). Skipping.", installItem.ItemName);
                return Enumerable.Empty<InstalledItem>();
            }
        }

        _logger.LogInformation("Installing file: {ItemName} from {SourcePath}", installItem.ItemName, sourcePath);

        // For file installations, we just track that they were processed
        // The actual file might be used by other processes or just needs to be validated
        // Create history marker
        var historyDir = Path.Combine(_config.SqlScriptsDir, "installhistory");
        Directory.CreateDirectory(historyDir);
        var historyFile = Path.Combine(historyDir, Path.GetFileName(sourcePath));
        await File.WriteAllTextAsync(historyFile, $"Installed at: {DateTime.UtcNow:O}\nSource: {sourcePath}");

        _logger.LogInformation("File installation completed: {ItemName}", installItem.ItemName);

        var installedItem = new InstalledItem
        {
            ID = 0,
            ItemName = installItem.ItemName,
            ItemType = installItem.Type,
            InstallTime = DateTime.UtcNow,
            Source = sourcePath,
            Target = null,
            SetName = setName,
            Version = 1
        };

        return new[] { installedItem };
    }

    private string GetFullPath(string source)
    {
        // Handle TFS-style paths ($/...) by stripping the $/ prefix
        if (source.StartsWith("$/"))
        {
            source = source.Substring(2);
        }

        if (!string.IsNullOrEmpty(_config.SqlScriptsPrefix) &&
            source.StartsWith(_config.SqlScriptsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Substring(_config.SqlScriptsPrefix.Length).TrimStart('/', '\\');
        }

        // Combine with base scripts directory
        var fullPath = Path.Combine(_config.SqlScriptsDir, source.Replace('/', Path.DirectorySeparatorChar));
        _logger.LogInformation("Full path for {Source}: {FullPath}", source, fullPath);
        return fullPath;
    }
}
