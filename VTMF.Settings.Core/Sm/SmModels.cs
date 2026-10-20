using System.Text.Json.Serialization;

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
    public int AvailableVersion { get; init; }
    public string State { get; init; } = SmModuleStates.NotInstalled;
}

public sealed class SmInstallResult
{
    public required string Client { get; init; }
    public required string PreviousState { get; init; }
    public int? PreviousVersion { get; init; }
    public int InstalledVersion { get; init; }
    public int ScriptsRun { get; init; }
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

public sealed class SmSession
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Process { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime LastActivity { get; set; }
    public string Status { get; set; } = string.Empty;
    public int PlanCount { get; set; }
    public int AppliedCount { get; set; }
}

/// <summary>Desired state passed to sm.CreatePlan; serialized with lowercase JSON names, nulls omitted.</summary>
public sealed class SmDesiredState
{
    [JsonPropertyName("process")]
    public string Process { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "merge";

    [JsonPropertyName("scope")]
    public IReadOnlyList<SmScopeEntry> Scope { get; init; } = [];

    [JsonPropertyName("rows")]
    public IReadOnlyList<SmDesiredRow> Rows { get; init; } = [];
}

public sealed class SmScopeEntry
{
    [JsonPropertyName("table")]
    public string Table { get; init; } = string.Empty;

    [JsonPropertyName("layer")]
    public string Layer { get; init; } = "base";
}

public sealed class SmDesiredRow
{
    [JsonPropertyName("table")]
    public string Table { get; init; } = string.Empty;

    [JsonPropertyName("layer")]
    public string Layer { get; init; } = "base";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("settingtype")]
    public string SettingType { get; init; } = "Setting";

    [JsonPropertyName("listvalue")]
    public string? ListValue { get; init; }

    [JsonPropertyName("ordernr")]
    public int? OrderNr { get; init; }

    [JsonPropertyName("op")]
    public string Op { get; init; } = "upsert";

    [JsonPropertyName("after")]
    public string? After { get; init; }
}

public sealed class SmPlanOptions
{
    [JsonPropertyName("createTable")]
    public bool CreateTable { get; init; }

    [JsonPropertyName("targetAlias")]
    public bool TargetAlias { get; init; }
}

public sealed class SmPlanItem
{
    public int Seq { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PhysicalTable { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string? WorkerCode { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? SettingType { get; set; }
    public string? ListValue { get; set; }
    public int? OrderNr { get; set; }
    public string? OldValue { get; set; }
    public string? OldSettingType { get; set; }
    public string? OldListValue { get; set; }
    public int? OldOrderNr { get; set; }
    public string? BackupTable { get; set; }
}

public sealed class SmPlanWarning
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>Result of sm.CreatePlan: Status is Created, NoChanges or DryRun; PlanId/Checksum are set only for Created.</summary>
public sealed class SmPlanResult
{
    public required int? PlanId { get; init; }
    public required string? Checksum { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<SmPlanItem> Items { get; init; }
    public required IReadOnlyList<SmPlanWarning> Warnings { get; init; }
}

public sealed class SmPlan
{
    public int Id { get; set; }
    public int? SessionId { get; set; }
    public int? RollbackOf { get; set; }
    public DateTime Created { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string Process { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public string? Message { get; set; }
}

public sealed class SmPlanDetails
{
    public required SmPlan Plan { get; init; }
    public required IReadOnlyList<SmPlanItem> Items { get; init; }
    public required IReadOnlyList<SmPlanWarning> Warnings { get; init; }
}

/// <summary>Result of sm.ApplyPlan: Status is Applied, Stale (Conflicts lists why) or Failed (Message holds the error).</summary>
public sealed class SmApplyResult
{
    public required int PlanId { get; init; }
    public required string Status { get; init; }
    public required string? Message { get; init; }
    public required IReadOnlyList<SmPlanConflict> Conflicts { get; init; }
}

/// <summary>An item whose precondition does not hold. Reason: changed, missing, exists, duplicate, table_exists, table_missing, table_not_empty.</summary>
public sealed class SmPlanConflict
{
    public int Seq { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PhysicalTable { get; set; } = string.Empty;
    public string? WorkerCode { get; set; }
    public string? Name { get; set; }
    public string? ExpectedValue { get; set; }
    public string? CurrentValue { get; set; }
    public string Reason { get; set; } = string.Empty;
}
