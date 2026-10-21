using System.Text.Json.Serialization;

namespace VTMF.Settings.Core.Sm.Features;

/// <summary>A feature document: header, the rows it sets, its parameters and manual steps.</summary>
public sealed class SmFeature
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Draft";

    [JsonPropertyName("builtOnProcess")]
    public string? BuiltOnProcess { get; init; }

    [JsonPropertyName("revision")]
    public long? Revision { get; init; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("created")]
    public DateTime? Created { get; init; }

    [JsonPropertyName("modifiedBy")]
    public string? ModifiedBy { get; init; }

    [JsonPropertyName("modified")]
    public DateTime? Modified { get; init; }

    [JsonPropertyName("rows")]
    public IReadOnlyList<SmFeatureRow> Rows { get; init; } = [];

    [JsonPropertyName("params")]
    public IReadOnlyList<SmFeatureParam> Params { get; init; } = [];

    [JsonPropertyName("steps")]
    public IReadOnlyList<SmFeatureStep> Steps { get; init; } = [];
}

public sealed class SmFeatureRow
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

    [JsonPropertyName("op")]
    public string Op { get; init; } = "set";

    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

public sealed class SmFeatureParam
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("default")]
    public string? Default { get; init; }

    [JsonPropertyName("values")]
    public IReadOnlyList<SmFeatureParamValue> Values { get; init; } = [];
}

public sealed class SmFeatureParamValue
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

public sealed class SmFeatureStep
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("sql")]
    public string? Sql { get; init; }
}

/// <summary>One line of a feature search.</summary>
public sealed class SmFeatureSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? BuiltOnProcess { get; set; }
    public string TableKinds { get; set; } = string.Empty;
    public int ParamCount { get; set; }
    public DateTime Modified { get; set; }
}

/// <summary>An applied feature joined with its current metabase entry; the metabase fields are null when the feature no longer exists.</summary>
public sealed class SmAppliedFeatureInfo
{
    public required SmAppliedFeature Applied { get; init; }
    public string? Description { get; init; }
    public string? CurrentName { get; init; }
    public string? Status { get; init; }
}

public sealed class SmFeatureSaveResult
{
    public required int Id { get; init; }
    public required long Revision { get; init; }
    public required IReadOnlyList<SmPlanWarning> Warnings { get; init; }
}

public sealed class SmFeaturePlanResult
{
    public required int FeatureId { get; init; }
    public required string FeatureName { get; init; }
    public required SmPlanResult Plan { get; init; }
    public required IReadOnlyList<SmPlanWarning> Warnings { get; init; }
    public required IReadOnlyList<SmFeatureStep> Steps { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
