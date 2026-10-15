namespace VTMF.Settings.Entities;

public record SettingFeatureValue
{
 
    public string FeatureName { get; set; } = null!;
    
    public string ParentSettingName { get; set; } = null!;
    public string? StoreTable { get; set; }

    public string SettingName { get; set; } = null!;
    public int Order { get; set; }
    public string? SettingValue { get; set; }

    public SettingValueType SettingValueType { get; set; }
    public bool PromptOnApply { get; set; }

    public SettingFeatureValue Configure(ProcessType forProcess)
    {
        var configured = this with { 
            SettingName = SettingName.Replace("{{processname}}", forProcess.Name)
                                     .Replace("{{clientName}}", forProcess.ClientName),
        };
        return configured;
    }
}
