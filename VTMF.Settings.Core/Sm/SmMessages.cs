namespace VTMF.Settings.Core.Sm;

internal static class SmMessages
{
    public static string NewerThanTool(string client, int? installedVersion, int toolVersion) =>
        $"The sm module in client '{client}' is version {installedVersion}, newer than this tool's version {toolVersion}. Update the VTMF.Settings MCP server image.";
}
