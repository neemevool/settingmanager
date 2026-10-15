using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Installers;

public class DatabaseInstaller : IInstaller
{
    private readonly DatabaseService _databaseService;
    private readonly SqlScriptParser _sqlParser;
    private readonly InstallConfig _config;
    private readonly ILogger<DatabaseInstaller> _logger;
    private Dictionary<string, int> _fileVersionCache = new();

    public DatabaseInstaller(
        DatabaseService databaseService,
        SqlScriptParser sqlParser,
        InstallConfig config,
        ILogger<DatabaseInstaller> logger)
    {
        _databaseService = databaseService;
        _sqlParser = sqlParser;
        _config = config;
        _logger = logger;
    }

    public enum VersionCacheSource 
    {
        CsvFile,
        Tfs
    }

    public async Task<bool> CanConnect()
    {
        return await _databaseService.TestServerConnectionAsync();
    }

    public async Task<bool> DatabaseIsCreated()
    {
        return await _databaseService.CheckDatabaseExists();
    }

    public async Task<bool> InitializeVersionCache(VersionCacheSource source = VersionCacheSource.CsvFile)
    {
        if (source == VersionCacheSource.CsvFile)
            return await InitializeVersionCacheFromCsv();

        if (source == VersionCacheSource.Tfs)
            return await InitializeVersionCacheFromTfs();

        return false;

    }
    private async Task<bool> InitializeVersionCacheFromTfs()
    {
        var accessToken = _config.TfsToken ?? Environment.GetEnvironmentVariable("TFS_ACCESS_TOKEN");
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("TFS access token is not configured. Set TfsToken in config or TFS_ACCESS_TOKEN environment variable.");
            return false;
        }

        const string vtmf = "vtmf.visualstudio.com";
        var source = "$/" + _config.SqlScriptsPrefix;
        var sourcePrefix = source + "/";
        var encodedSource = Uri.EscapeDataString(source);
        var downloadUri = $"https://{vtmf}/DefaultCollection/_apis/tfvc/items?scopePath={encodedSource}&recursionLevel=Full&api-version=1.0";

        using var httpClient = new HttpClient();
        var authBytes = System.Text.Encoding.ASCII.GetBytes($":{accessToken}");
        var authHeader = Convert.ToBase64String(authBytes);
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        try
        {
            var response = await httpClient.GetAsync(downloadUri);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("value", out var valueArray))
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    var isFolder = item.TryGetProperty("isFolder", out var isFolderProp) && isFolderProp.GetBoolean();
                    if (!isFolder)
                    {
                        if (item.TryGetProperty("version", out var versionProp) &&
                            item.TryGetProperty("path", out var pathProp))
                        {
                            var version = versionProp.GetInt32();
                            //var path = pathProp.GetString()?.Replace(sourcePrefix, "", StringComparison.OrdinalIgnoreCase) ?? string.Empty;
                            var path = pathProp.GetString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(path))
                            {
                                _fileVersionCache[path] = version;
                                
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} file versions from TFS", _fileVersionCache.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize version cache from TFS");
            return false;
        }
    }
    private async Task<bool> InitializeVersionCacheFromCsv()
    {
        var csvFilePath = Path.Combine(_config.SqlScriptsDir, "versions.csv");

        if (!File.Exists(csvFilePath))
        {
            _logger.LogError("CSV file not found: {CsvFilePath}. Failed to initialize version cache from CSV.", csvFilePath);
            return false;
        }
        _logger.LogInformation("Loaded {Count} file versions from CSV", _fileVersionCache.Count);
        var lines = await File.ReadAllLinesAsync(csvFilePath);

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var version))
            {
                var filePath = parts[0].Trim().Trim('"');
                _fileVersionCache[filePath] = version;
            }
        }
        return true;
    }

    // IInstaller contract. Delegates to the overload that can optionally restrict
    // a folder install to a specific subset of files.
    public Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName)
        => InstallAsync(installItem, setName, null);

    /// <summary>
    /// Installs a database item. When <paramref name="sqlFileNames"/> is provided and the item is a
    /// folder, only the files whose name appears in the list are installed (matched case-insensitively
    /// on file name); otherwise every <c>*.txt</c> file in the folder is installed.
    /// </summary>
    public async Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName, List<string>? sqlFileNames)
    {
        _logger.LogInformation("Processing database installation: {ItemName} (check: {Check}, isFolder: {IsFolder})",
        installItem.ItemName, installItem.Check, installItem.IsFolder);

        var installedItems = new List<InstalledItem>();

        if (installItem.IsFolder)
        {
            installedItems.AddRange(await InstallFolderAsync(installItem, setName, sqlFileNames));
        }
        else
        {
            var item = await InstallSingleScriptAsync(installItem, setName);
            if (item != null)
            {
                installedItems.Add(item);
            }
        }

        return installedItems;
    }

    private async Task<IEnumerable<InstalledItem>> InstallFolderAsync(InstallItem installItem, string setName, List<string>? sqlFileNames = null)
    {
        var folderPath = GetFullPath(installItem);
        _logger.LogDebug("Installing scripts from folder: {FolderPath}", folderPath);
        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning("Folder not found: {FolderPath}. Skipping.", folderPath);
            return Enumerable.Empty<InstalledItem>();
        }

        var sqlFiles = Directory.GetFiles(folderPath, "*.txt")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sqlFileNames is { Count: > 0 })
        {
            var requested = new HashSet<string>(sqlFileNames, StringComparer.OrdinalIgnoreCase);
            sqlFiles = sqlFiles
                .Where(f => requested.Contains(Path.GetFileName(f)))
                .ToList();
            _logger.LogInformation("Restricting folder install to {RequestedCount} requested file(s); {MatchedCount} matched in {FolderPath}",
                requested.Count, sqlFiles.Count, folderPath);
        }

        _logger.LogInformation("Found {Count} SQL files in folder: {FolderPath}", sqlFiles.Count, folderPath);

        var installedItems = new List<InstalledItem>();

        foreach (var sqlFile in sqlFiles)
        {
            
            try
            {
                //var fileInstallItemRelativePath = Path.GetRelativePath(_config.SqlScriptsDir, sqlFile);
                var fileInstallItemRelativePath = Path.GetFileName(sqlFile);
                InstallItem fileInstallItem = installItem with
                {
                    FullPath = sqlFile,
                    RelativePath = fileInstallItemRelativePath,
                    Source = $"{installItem.Source}/{fileInstallItemRelativePath}"
                };
                _logger.LogDebug("Script: {ScriptFile} (relative path: {RelativePath})", sqlFile, fileInstallItemRelativePath);
                var item = await InstallScriptFileAsync(fileInstallItem, setName);
                if (item != null)
                {
                    installedItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install script: {ScriptFile}", sqlFile);
                throw new InvalidOperationException($"Installation failed for script: {sqlFile}", ex);
            }
        }

        return installedItems;
    }

    private async Task<InstalledItem?> InstallSingleScriptAsync(InstallItem installItem, string setName)
    {
        var sqlFile = GetFullPath(installItem);

        if (!File.Exists(sqlFile))
        {
            _logger.LogWarning("Script file not found: {ScriptPath}. Skipping.", sqlFile);
            return null;
        }
        var fileInstallItemRelativePath = Path.GetFileName(sqlFile);
        InstallItem fileInstallItem = installItem with
        {
            FullPath = sqlFile,
            RelativePath = fileInstallItemRelativePath,
            Source = $"{installItem.Source}/{fileInstallItemRelativePath}"

        };
        return await InstallScriptFileAsync(fileInstallItem, setName);
    }

    private async Task<InstalledItem?> InstallScriptFileAsync(
        InstallItem installItem, string setName)
    {
        
        

        var scriptPath = installItem.FullPath;
        var relativePath = installItem.RelativePath;
        //Reading script: /data/printservice/025-table-printers.txt
        _logger.LogInformation("Reading script: {FullPath}", scriptPath);
        var scriptContent = await File.ReadAllTextAsync(scriptPath);
        
        var version = GetFileVersion(installItem.Source) ?? 0;
        if (version == 0)
            version = GetFileVersion(relativePath) ?? 0;

        //Processing script: /data/printservice/025-table-printers.txt->printservice/025-table-printers.txt(item: printservice/025-table-printers.txt)
        _logger.LogInformation("Processing script: {ScriptPath} check: {Check} (item: {Source})", scriptPath, installItem.Check, installItem.Source);

        var installedItem = new InstalledItem
        {
            ID = 0,
            ItemName = installItem.ItemName,
            ItemType = installItem.Type,
            InstallTime = DateTime.UtcNow,
            Source = scriptPath,
            Target = null,
            SetName = setName,
            Version = version
        };

        var installedVersion = await _databaseService.CheckInstallAsync(installItem);
        if (installedVersion.HasValue && installedVersion.Value >= version)
        {
            _logger.LogInformation("Script {ItemName} already installed (installed: v{InstalledVersion}, script: v{ScriptVersion}). Skipping.",
                installItem.ItemName, installedVersion.Value, version);
            installedItem = installedItem with { Status = InstallStatus.Skipped, Version = installedVersion.Value };
            return installedItem;
        }

        if (installItem.Mode == InstallMode.Validate) {             
            _logger.LogInformation("Validation mode for {ItemName}. Marking as should be installed.", installItem.ItemName);
            installedItem = installedItem with { Status = InstallStatus.ShouldBeInstalled };
            return installedItem;
        }

        // Execute the script in batches
        var batches = _sqlParser.SplitIntoBatches(scriptContent).ToList();
        //_logger.LogInformation("Executing {BatchCount} SQL batches for {ItemName}", batches.Count, itemName);
        try
        {
            foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
            {
                //_logger.LogDebug("Executing batch {Index}/{Total} for {ItemName}", index + 1, batches.Count, itemName);
                await _databaseService.ExecuteSqlBatchAsync(batch);
            }

            //_logger.LogInformation("Successfully executed all batches for {ItemName}", itemName);

            // Register installation based on check method
            await _databaseService.RegisterInstallationAsync(
                    installItem, setName, version);
        } catch (Exception ex) {
            installedItem = installedItem with
            {
                Status = InstallStatus.Failed,
                ErrorMessage = ex.Message
            };
            return installedItem;
        }

        return installedItem with { Status = InstallStatus.Installed, Version = version };

    }

    private string GetFullPath(InstallItem item)
    {
        return FileUtils.GetFullPath(item, _config);
    }


    private int? GetFileVersion(string relativePath)
    {
        if (_fileVersionCache.TryGetValue(relativePath, out var cachedVersion))
        {
            return cachedVersion;
        }
        return null;
    }
}
