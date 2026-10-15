namespace VTMF.Settings.Entities;

public record SettingDescription(string SettingName, string Description)
{
    public SettingDescription() : this(string.Empty, string.Empty) 
    { 
    }
    public string Description { get; set; } = Description;
    public string SettingName { get; set; } = SettingName;
}
