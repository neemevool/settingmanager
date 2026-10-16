namespace VTMF.Settings.Core.Sm;

public static class SmErrorCodes
{
    public const string ModuleNotInstalled = "module_not_installed";
    public const string InvalidName = "invalid_name";
    public const string InvalidLayer = "invalid_layer";
    public const string ProcessNotFound = "process_not_found";
    public const string TableNotFound = "table_not_found";
    public const string LayerNotSupported = "layer_not_supported";
    public const string SmError = "sm_error";

    internal static string FromSqlErrorNumber(int number) => number switch
    {
        51001 => InvalidName,
        51002 => InvalidLayer,
        51003 => ProcessNotFound,
        51004 => TableNotFound,
        51005 => LayerNotSupported,
        _ => SmError,
    };
}
