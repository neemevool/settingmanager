namespace VTMF.Settings.Entities;

public record NewSettingArgs(string FeatureName, string NewSettingName, PositionOption position, string AfterSettingName);

public enum PositionOption
{
    First,
    Last,
    After,
    None
}
