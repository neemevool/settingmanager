namespace VTMF.Settings.Core.Sm.Usage;

/// <summary>A process relevant to a usage query: the requested process and its parent (in scope), plus SETTINGPROCEDURES targets.</summary>
internal sealed class SmUsageProcess
{
    public string Name { get; set; } = string.Empty;
    public string? SettingProcedures { get; set; }
    public bool InScope { get; set; }
}

/// <summary>A candidate stored-procedure module and its source.</summary>
internal sealed class SmUsageModule
{
    public string ProcName { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

/// <summary>A ServerProcessSettings row split into process and setting name. <see cref="Value"/> is already masked; <see cref="IsEmpty"/> is true for NULL or blank VALUE (computed in SQL).</summary>
internal sealed class SmUsageSetting
{
    public string Process { get; set; } = string.Empty;
    public string SettingName { get; set; } = string.Empty;
    public string WorkerCode { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsEmpty { get; set; }
}

internal sealed record SmUsageSources(
    IReadOnlyList<SmUsageProcess> Processes,
    IReadOnlyList<SmUsageModule> Modules,
    IReadOnlyList<SmUsageSetting> Settings);
