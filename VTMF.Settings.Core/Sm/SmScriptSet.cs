using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VTMF.Settings.Core.Sm;

public sealed record SmScript(string Name, string Text);

/// <summary>The versioned set of sm module scripts plus the manifest recorded next to them.</summary>
public sealed class SmScriptSet
{
    public const string ClientModule = "settingsmanagement";

    public const string MetabaseModule = "settingsmetabase";

    private const string ManifestFileName = "module.json";

    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

    private SmScriptSet(string moduleName, int version, string recordedSha256, IReadOnlyList<SmScript> scripts)
    {
        ModuleName = moduleName;
        Version = version;
        RecordedSha256 = recordedSha256;
        Scripts = scripts;
    }

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Sql", ClientModule);

    public string ModuleName { get; }

    public int Version { get; }

    public string RecordedSha256 { get; }

    public IReadOnlyList<SmScript> Scripts { get; }

    public static SmScriptSet LoadDefault() => Load(DefaultDirectory, ClientModule);

    public static SmScriptSet LoadMetabase() => Load(Path.Combine(AppContext.BaseDirectory, "Sql", MetabaseModule), MetabaseModule);

    public static SmScriptSet Load(string directory, string moduleName)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"sm script directory not found: {directory}");
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"sm module manifest not found: {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), ManifestOptions)
            ?? throw new InvalidOperationException($"sm module manifest is empty: {manifestPath}");

        var scripts = Directory.EnumerateFiles(directory, "*.txt")
            .Select(path => new SmScript(Path.GetFileName(path), File.ReadAllText(path)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        return new SmScriptSet(moduleName, manifest.Version, manifest.Sha256 ?? string.Empty, scripts);
    }

    public string ComputeSha256()
    {
        var sb = new StringBuilder();
        foreach (var script in Scripts)
        {
            sb.Append(script.Name).Append('\n')
                .Append(script.Text.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed record Manifest(int Version, string? Sha256);
}
