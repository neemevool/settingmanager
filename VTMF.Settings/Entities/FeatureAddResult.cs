namespace VTMF.Settings.Entities;

public record FeatureAddResult(bool Success, string Message, ReloadOption Reload)
{
    public string? ParentSettingName { get; set; }
}

public enum ReloadOption
{
    None,
    ReloadSetting,
    ReloadAllSettings,
    ReloadFromParent
}
