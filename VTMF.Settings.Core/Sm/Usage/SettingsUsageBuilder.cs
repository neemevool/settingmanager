using System.Globalization;

namespace VTMF.Settings.Core.Sm.Usage;

/// <summary>Assembles setting-usage rows from raw sources (C# port of the row assembly in sm.GetSettingsUsage).</summary>
internal static class SettingsUsageBuilder
{
    private const string AllWorkers = "ALL";
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<SmSettingUsage> Build(string process, string? namePrefix, SmUsageSources sources)
    {
        var inScope = new HashSet<string>(sources.Processes.Where(p => p.InScope).Select(p => p.Name), Cmp);

        var usage = new List<(SmSettingUsage Row, string? LegacyProcess)>();
        foreach (var module in sources.Modules)
        {
            var scan = SettingsUsageScanner.Scan(module.Definition);
            var attribution = Attribute(scan.LiteralProcess, module.ProcName, inScope);

            if (attribution is not null)
            {
                foreach (var read in scan.PsvReads)
                    usage.Add((Row(read.SettingName, module.ProcName, "psv", attribution, read.DefaultRaw), null));
            }

            foreach (var read in scan.LegacyReads)
            {
                var literal = read.ProcessArg.StartsWith('@') ? null : SqlLiteral.Unquote(read.ProcessArg);
                var legacyAttribution = Attribute(literal, module.ProcName, inScope);
                if (legacyAttribution is null) continue;
                var target = literal ?? process;
                var legacyProcess = sources.Processes.FirstOrDefault(p => Cmp.Equals(p.Name, target))?.SettingProcedures ?? target;
                usage.Add((Row(read.SettingName, module.ProcName, "legacy", legacyAttribution, read.DefaultRaw), legacyProcess));
            }

            if (attribution is not null && scan.Unparsed > 0)
                usage.Add((new SmSettingUsage
                {
                    ProcedureName = module.ProcName,
                    CallStyle = "unparsed",
                    Attribution = attribution,
                    Detail = scan.Unparsed.ToString(CultureInfo.InvariantCulture),
                }, null));

            if (attribution is not null && scan.HasDirectRead)
                usage.Add((new SmSettingUsage
                {
                    ProcedureName = module.ProcName,
                    CallStyle = "direct",
                    Attribution = attribution,
                    Detail = "ServerProcessSettings",
                }, null));
        }

        // Effective value as dbo.GetProcessSettingValues resolves it: process over parent including empty values,
        // then keep the winner only when non-empty (an empty process value hides the parent).
        var cfg = sources.Settings
            .Where(s => inScope.Contains(s.Process) && Cmp.Equals(s.WorkerCode, AllWorkers))
            .GroupBy(s => s.SettingName, Cmp)
            .Select(g => g.OrderBy(s => Cmp.Equals(s.Process, process) ? 0 : 1).First())
            .Where(s => !s.IsEmpty)
            .ToDictionary(s => s.SettingName, s => (s.Value, ConfiguredIn: Cmp.Equals(s.Process, process) ? "process" : "parent"), Cmp);

        var workers = sources.Settings
            .Where(s => Cmp.Equals(s.Process, process) && !Cmp.Equals(s.WorkerCode, AllWorkers))
            .GroupBy(s => s.SettingName, Cmp)
            .ToDictionary(g => g.Key, g => g.Count(), Cmp);

        var used = new HashSet<string>(usage.Where(u => u.Row.SettingName is not null).Select(u => u.Row.SettingName!), Cmp);
        foreach (var (name, c) in cfg)
        {
            if (c.ConfiguredIn == "process" && !used.Contains(name))
                usage.Add((new SmSettingUsage { SettingName = name, CallStyle = "none", Unused = true }, null));
        }

        foreach (var (row, legacyProcess) in usage)
        {
            if (row.CallStyle == "legacy")
            {
                var match = sources.Settings.FirstOrDefault(s =>
                    Cmp.Equals(s.Process, legacyProcess) && Cmp.Equals(s.SettingName, row.SettingName)
                    && Cmp.Equals(s.WorkerCode, AllWorkers) && !s.IsEmpty);
                row.ConfiguredValue = match?.Value;
                row.ConfiguredIn = match is null ? null : Cmp.Equals(legacyProcess, process) ? "process" : "parent";
                row.WorkerOverrides = sources.Settings.Count(s =>
                    Cmp.Equals(s.Process, legacyProcess) && Cmp.Equals(s.SettingName, row.SettingName)
                    && !Cmp.Equals(s.WorkerCode, AllWorkers));
            }
            else if (row.SettingName is not null)
            {
                if (cfg.TryGetValue(row.SettingName, out var c))
                    (row.ConfiguredValue, row.ConfiguredIn) = (c.Value, c.ConfiguredIn);
                row.WorkerOverrides = workers.GetValueOrDefault(row.SettingName);
            }
        }

        return usage
            .Select(u => u.Row)
            .Where(r => namePrefix is null
                        || (r.SettingName is not null && r.SettingName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.SettingName is null ? 1 : 0)
            .ThenBy(r => r.SettingName, Cmp)
            .ThenBy(r => r.ProcedureName is null ? 0 : 1)
            .ThenBy(r => r.ProcedureName, Cmp)
            .ThenBy(r => r.CallStyle, Cmp)
            .ToList();
    }

    private static string? Attribute(string? literalProcess, string procName, HashSet<string> inScope)
    {
        if (literalProcess is not null)
            return inScope.Contains(literalProcess) ? "literal" : null;
        return inScope.Any(p => procName.Contains(p, StringComparison.OrdinalIgnoreCase)) ? "procname" : null;
    }

    private static SmSettingUsage Row(string settingName, string procName, string callStyle, string attribution, string defaultRaw) =>
        new()
        {
            SettingName = settingName,
            ProcedureName = procName,
            CallStyle = callStyle,
            Attribution = attribution,
            DefaultInCode = SqlLiteral.Unquote(defaultRaw),
        };
}
