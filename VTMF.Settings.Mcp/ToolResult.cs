using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Mcp;

public static class ToolErrorCodes
{
    public const string UnknownClient = "unknown_client";
}

public sealed record ToolError(string Code, string Message);

public sealed record ToolResult<T>(bool Ok, T? Data, ToolError? Error);

public static class ToolResult
{
    public static ToolResult<T> Success<T>(T data) => new(true, data, null);

    public static ToolResult<T> Failure<T>(string code, string message) => new(false, default, new ToolError(code, message));

    /// <summary>Runs a tool body and turns expected failures into structured results instead of server faults.</summary>
    public static async Task<ToolResult<T>> RunAsync<T>(Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return Success(await body().ConfigureAwait(false));
        }
        catch (SmException ex)
        {
            return Failure<T>(ex.Code, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Failure<T>(ToolErrorCodes.UnknownClient, ex.Message);
        }
    }
}
