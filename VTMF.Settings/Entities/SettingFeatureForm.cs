namespace VTMF.Settings.Entities;

public class SettingFeatureForm
{
    public string ProcessTypeName { get; set; } = string.Empty;
    public string ParentSettingName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string FeatureType { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Convert form data to SettingFeature record for database operations
    /// </summary>
    public SettingFeature ToSettingFeature()
    {
        return new SettingFeature
        {
            ProcessTypeName = ProcessTypeName,
            ParentSettingName = ParentSettingName,
            TableName = TableName,
            FeatureName = FeatureName,
            Label = Label,
            ActionType = ActionType,
            FeatureType = FeatureType,
            Description = Description
        };
    }

    /// <summary>
    /// Create form from SettingFeature record
    /// </summary>
    public static SettingFeatureForm FromSettingFeature(SettingFeature feature)
    {
        return new SettingFeatureForm
        {
            ProcessTypeName = feature.ProcessTypeName,
            ParentSettingName = feature.ParentSettingName,
            TableName = feature.TableName,
            FeatureName = feature.FeatureName,
            Label = feature.Label,
            ActionType = feature.ActionType,
            FeatureType = feature.FeatureType,
            Description = feature.Description
        };
    }
}
