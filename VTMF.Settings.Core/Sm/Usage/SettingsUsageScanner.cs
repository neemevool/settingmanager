using System.Text.RegularExpressions;

namespace VTMF.Settings.Core.Sm.Usage;

internal sealed record SmPsvRead(string SettingName, string DefaultRaw);

internal sealed record SmLegacyRead(string ProcessArg, string SettingName, string DefaultRaw);

internal sealed record SmModuleScan(
    string? LiteralProcess,
    IReadOnlyList<SmPsvRead> PsvReads,
    IReadOnlyList<SmLegacyRead> LegacyReads,
    int Unparsed,
    bool HasDirectRead);

/// <summary>Scans stored-procedure source for backend setting reads (C# port of the REGEXP_* parsing in sm.GetSettingsUsage).</summary>
internal static partial class SettingsUsageScanner
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"GetPSV[IB]?\s*\(\s*@\w+\s*,\s*N?'([^']+)'\s*,\s*(N?'(?:[^']|'')*'|[^)]*)\)", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Psv();

    [GeneratedRegex(@"GetProcessSettingValue(?:Int|Dec)?\s*\(\s*(N?'\w+'|@\w+)\s*,\s*N?'([^']+)'\s*,\s*[^,]+,\s*(N?'(?:[^']|'')*'|[^)]*)\)", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Legacy();

    [GeneratedRegex(@"GetPSV[IB]?\s*\(", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PsvCall();

    [GeneratedRegex(@"GetProcessSettingValue(?:Int|Dec)?\s*\(", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex LegacyCall();

    [GeneratedRegex(@"from\s+(?:\[?dbo\]?\.)?\[?ServerProcessSettings\]?", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Direct();

    [GeneratedRegex(@"GetProcessSettingValues\s*\(\s*N?'(\w+)'", Opts, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Literal();

    public static SmModuleScan Scan(string definition)
    {
        var psv = Psv().Matches(definition)
            .Select(m => new SmPsvRead(m.Groups[1].Value, m.Groups[2].Value)).ToList();
        var legacy = Legacy().Matches(definition)
            .Select(m => new SmLegacyRead(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToList();
        var calls = PsvCall().Count(definition) + LegacyCall().Count(definition);
        var literal = Literal().Match(definition);
        return new SmModuleScan(
            literal.Success ? literal.Groups[1].Value : null,
            psv,
            legacy,
            calls - psv.Count - legacy.Count,
            Direct().IsMatch(definition));
    }
}
