namespace VTMF.Settings.Core.Sm;

public static class SmModuleStates
{
    public const string NotInstalled = "not_installed";
    public const string UpToDate = "up_to_date";
    public const string Outdated = "outdated";
    public const string NewerThanTool = "newer_than_tool";

    public static string From(int? installedVersion, int availableVersion) => installedVersion switch
    {
        null => NotInstalled,
        var v when v < availableVersion => Outdated,
        var v when v > availableVersion => NewerThanTool,
        _ => UpToDate,
    };
}
