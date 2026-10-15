namespace VTMF.Settings.Entities;

public class SettingMeta
{
    public required string SettingName { get; set; }
    public List<string> Values { get; set; } = new();
    public SettingValueType SettingValueType { get; set; }
}
