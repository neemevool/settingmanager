namespace VTMF.Settings.Entities;

public record SettingFeature
{
    
    public string ProcessTypeName { get; init; } = null!;
    public string ParentSettingName { get; init; } = null!;
    public string TableName { get; init; } = null!;
    public string FeatureName { get; init; } = null!;
    public string Label { get; init; } = null!;
    public string ActionType { get; init; } = "replace";
    public string FeatureType { get; init; } = null!;
    public string? Description { get; init; }

    public TableInfo TableInfo => new TableInfo(TableName ?? string.Empty);

}
public static class FeatureTypes
{
    public const string SettingGroup = "SettingGroup";
    public const string SettingReplace = "SettingReplace";
    public const string SettingUpdate = "SettingUpdate";
    public const string SettingMove = "SettingMove";
}