using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Installers;
internal static class FileUtils
{
    internal static string GetFullPath(InstallItem item, InstallConfig config)
    {
        var source = item.Source;

        var relativePath = GetRelativePath(item, config);

        var fullPath = Path.Combine(config.SqlScriptsDir, relativePath);
        
        return fullPath;
    }

    internal static string GetRelativePath(InstallItem item, InstallConfig config)
    {
        var source = item.Source;
        // Handle TFS-style paths ($/...) by stripping the $/ prefix
        if (source.StartsWith("$/"))
        {
            source = source.Substring(2);
        }
        // remove from the beginning _config.SqlScriptsPrefix if present
        if (!string.IsNullOrEmpty(config.SqlScriptsPrefix) &&
            source.StartsWith(config.SqlScriptsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Substring(config.SqlScriptsPrefix.Length).TrimStart('/', '\\');
        }

        if (source.StartsWith(config.SqlScriptsDir, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Substring(config.SqlScriptsDir.Length).TrimStart('/', '\\');
        }
        // Combine with base scripts directory
        return source.Replace('/', Path.DirectorySeparatorChar);
    }
}
