namespace VTMF.Settings.Core.Sm.Usage;

/// <summary>C# port of sm.UnquoteSqlLiteral.</summary>
internal static class SqlLiteral
{
    public static string? Unquote(string? s)
    {
        if (s is null) return null;
        var t = s.Trim();
        if (t.Length >= 3 && t.StartsWith("N'", StringComparison.Ordinal) && t.EndsWith('\''))
            t = t[1..];
        if (t.Length >= 2 && t[0] == '\'' && t[^1] == '\'')
            return t[1..^1].Replace("''", "'", StringComparison.Ordinal);
        return t;
    }
}
