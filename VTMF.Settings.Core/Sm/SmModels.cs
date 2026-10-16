namespace VTMF.Settings.Core.Sm;

public sealed class SmModuleInfo
{
    public string Module { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime Installed { get; set; }
}

public sealed class SmModuleStatus
{
    public bool Installed { get; init; }
    public int? Version { get; init; }
}

public sealed class SmProcess
{
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string ProcessType { get; set; } = string.Empty;
    public string? DataProcedures { get; set; }
    public string? SettingProcedures { get; set; }
    public string? Environment { get; set; }
    public int? OrderNr { get; set; }
    public string? Variants { get; set; }
    public int TableCount { get; set; }
}

public sealed class SmTable
{
    public string View { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string PhysicalTable { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string? OverrideProcedure { get; set; }
    public bool OverrideActive { get; set; }
}

public sealed class SmSettingRow
{
    public string Table { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
}

public sealed class SmEffectiveInfo
{
    public string? PhysicalTable { get; set; }
    public bool AliasUsed { get; set; }
    public string? OverrideProcedure { get; set; }
    public string? AdditionalProcedure { get; set; }
}

public sealed class SmEffectiveRow
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class SmEffectiveSettings
{
    public required SmEffectiveInfo Info { get; init; }
    public required IReadOnlyList<SmEffectiveRow> Rows { get; init; }
}

public sealed class SmSettingUsage
{
    public string? SettingName { get; set; }
    public string? ProcedureName { get; set; }
    public string CallStyle { get; set; } = string.Empty;
    public string? Attribution { get; set; }
    public string? DefaultInCode { get; set; }
    public string? ConfiguredValue { get; set; }
    public string? ConfiguredIn { get; set; }
    public int WorkerOverrides { get; set; }
    public bool Unused { get; set; }
    public string? Detail { get; set; }
}

public sealed class SmLegacyEnvRow
{
    public string PhysicalTable { get; set; } = string.Empty;
    public string View { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string SettingType { get; set; } = string.Empty;
    public string? ListValue { get; set; }
    public int OrderNr { get; set; }
    public bool OverlayExists { get; set; }
}
