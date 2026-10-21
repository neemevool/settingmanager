using System.Text.RegularExpressions;

namespace VTMF.Settings.Core.Sm.Features;

/// <summary>Finds and substitutes <c>{{name}}</c> placeholders (matched case-insensitively against parameter names).</summary>
public static partial class FeaturePlaceholders
{
    /// <summary>Placeholders the tool fills in itself; never asked for and never declared as parameters.</summary>
    public static IReadOnlySet<string> BuiltIns { get; } =
        new HashSet<string>(["processname", "clientname"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> Find(string? text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            found.Add(match.Groups[1].Value);
        }

        return found;
    }

    /// <summary>Replaces every known placeholder; unknown ones are left untouched.</summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, string> values)
    {
        return PlaceholderRegex().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return match.Value;
        });
    }

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9_]*)\}\}")]
    private static partial Regex PlaceholderRegex();
}
