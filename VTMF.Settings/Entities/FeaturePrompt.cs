namespace VTMF.Settings.Entities;

public record FeaturePrompt(string TableName, string SettingName, string SettingValue, bool PromptOnApply)
{
    public string SettingValue { get; set; } = SettingValue;
}


