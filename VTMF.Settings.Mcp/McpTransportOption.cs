namespace VTMF.Settings.Mcp;

public enum McpTransport
{
    Stdio,
    Http,
}

public static class McpTransportOption
{
    public static McpTransport Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? value = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--transport=", StringComparison.OrdinalIgnoreCase))
                value = args[i]["--transport=".Length..];
            else if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];
        }

        return value?.ToUpperInvariant() switch
        {
            null or "STDIO" => McpTransport.Stdio,
            "HTTP" => McpTransport.Http,
            _ => throw new ArgumentException($"Unknown transport '{value}'. Use stdio or http.", nameof(args)),
        };
    }
}
