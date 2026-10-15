namespace VTMF.Settings.Entities;

public static class SettingExtensions
{
    public static string GetKey(this Setting setting, ProcessType processType)
    {
        return $"{processType.ProcessTypeName}:{setting.StoreTable}:{setting.Name}";
    }
}
