namespace VTMF.Settings.Core.Sm;

public static class SmErrorCodes
{
    public const string ModuleNotInstalled = "module_not_installed";
    public const string InvalidName = "invalid_name";
    public const string InvalidLayer = "invalid_layer";
    public const string ProcessNotFound = "process_not_found";
    public const string TableNotFound = "table_not_found";
    public const string LayerNotSupported = "layer_not_supported";
    public const string InstallFailed = "install_failed";
    public const string ModuleOutdated = "module_outdated";
    public const string ModuleNewerThanTool = "module_newer_than_tool";
    public const string InvalidDesired = "invalid_desired";
    public const string ServerSetting = "serversetting";
    public const string WorkerLayer = "worker_layer";
    public const string AliasTarget = "alias_target";
    public const string TableMissing = "table_missing";
    public const string DuplicateRow = "duplicate_row";
    public const string SessionNotFound = "session_not_found";
    public const string SessionClosed = "session_closed";
    public const string PlanNotFound = "plan_not_found";
    public const string PlanStatus = "plan_status";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string SmError = "sm_error";

    internal static string FromSqlErrorNumber(int number) => number switch
    {
        51001 => InvalidName,
        51002 => InvalidLayer,
        51003 => ProcessNotFound,
        51004 => TableNotFound,
        51005 => LayerNotSupported,
        51006 => InvalidDesired,
        51007 => ServerSetting,
        51008 => WorkerLayer,
        51009 => AliasTarget,
        51010 => TableMissing,
        51011 => DuplicateRow,
        51012 => SessionNotFound,
        51013 => SessionClosed,
        51014 => PlanNotFound,
        51015 => PlanStatus,
        51016 => ChecksumMismatch,
        _ => SmError,
    };
}
