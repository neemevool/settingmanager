using Microsoft.Data.SqlClient;

namespace VTMF.Settings.Core.Sm;

public sealed class SmException : Exception
{
    private const int CouldNotFindStoredProcedure = 2812;

    public SmException()
        : this(SmErrorCodes.SmError, "Settings management error.")
    {
    }

    public SmException(string message)
        : this(SmErrorCodes.SmError, message)
    {
    }

    public SmException(string message, Exception innerException)
        : this(SmErrorCodes.SmError, message, innerException)
    {
    }

    public SmException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    internal static bool TryTranslate(SqlException exception, out SmException translated)
    {
        if (exception.Number == CouldNotFindStoredProcedure && exception.Message.Contains("'sm.", StringComparison.OrdinalIgnoreCase))
        {
            translated = new SmException(SmErrorCodes.ModuleNotInstalled,
                "The settings management module (sm) is not installed in this client database. Install set 'settingsmanagement' with the VTMF.Settings installer.",
                exception);
            return true;
        }

        if (exception.Number is >= 51000 and <= 51999)
        {
            translated = new SmException(SmErrorCodes.FromSqlErrorNumber(exception.Number), exception.Message, exception);
            return true;
        }

        translated = null!;
        return false;
    }
}
