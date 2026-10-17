namespace VTMF.Settings.Tests.Sql;

internal static class Database5Scripts
{
    public static string Root =>
        Environment.GetEnvironmentVariable("DATABASE5_PATH") ?? @"C:\PROJECTS\VTMF\SAWMILL2\Sawmill\database5";

    // Minimal subset of the "initialdatabase"/"base" sets the sm module depends on, in dependency order.
    public static IEnumerable<string> BaseScripts =>
    [
        Path.Combine(Root, "init", "001-script-worker.txt"),
        Path.Combine(Root, "worker", "002-table-processtypes.txt"),
        Path.Combine(Root, "settings", "024-table-ServerProcessSettings.txt"),
        Path.Combine(Root, "settings", "034-script-processSettingstype.txt"),
        Path.Combine(Root, "settings", "025-function-GetProcessSettingValue.txt"),
        Path.Combine(Root, "settings", "035-function-GetProcessSettingValues.txt"),
        Path.Combine(Root, "settings", "036-function-GetPSV.txt"),
        Path.Combine(Root, "settings", "037-function-GetPSVI.txt"),
        Path.Combine(Root, "settings", "038-function-GetPSVB.txt"),
    ];

}
