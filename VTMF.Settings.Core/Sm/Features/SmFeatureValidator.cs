using System.Globalization;
using System.Text.RegularExpressions;

namespace VTMF.Settings.Core.Sm.Features;

public sealed record SmFeatureValidation(IReadOnlyList<string> Errors, IReadOnlyList<SmPlanWarning> Warnings);

/// <summary>Pure validation of a feature document: save rules always, finish rules when <c>finish</c> is set.</summary>
public static partial class SmFeatureValidator
{
    private const int MaxNameLength = 100;

    private static readonly string[] ParamTypes = ["Text", "Number", "Boolean", "List", "Position"];

    public static SmFeatureValidation Validate(SmFeature feature, bool finish)
    {
        var errors = new List<string>();
        var warnings = new List<SmPlanWarning>();

        ValidateSave(feature, errors);
        if (finish)
        {
            ValidateFinish(feature, errors);
        }

        ReportUnusedParams(feature, warnings);
        return new SmFeatureValidation(errors, warnings);
    }

    private static void ValidateSave(SmFeature feature, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(feature.Name))
        {
            errors.Add("Feature name is required.");
        }
        else if (feature.Name.Length > MaxNameLength)
        {
            errors.Add($"Feature name is longer than {MaxNameLength} characters.");
        }

        for (var i = 0; i < feature.Rows.Count; i++)
        {
            var row = feature.Rows[i];
            var label = $"Row {i + 1}";

            if (string.IsNullOrWhiteSpace(row.Table))
            {
                errors.Add($"{label} table is required.");
            }
            else if (row.Table.Contains("{{", StringComparison.Ordinal))
            {
                errors.Add($"{label} table cannot contain a placeholder.");
            }

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add($"{label} name is required.");
            }

            if (!LayerRegex().IsMatch(row.Layer ?? string.Empty))
            {
                errors.Add($"{label} layer '{row.Layer}' must be base, env:<name> or lang:<name>.");
            }

            var validOp = row.Op is "set" or "remove";
            if (!validOp)
            {
                errors.Add($"{label} op '{row.Op}' must be set or remove.");
            }

            if (row.SettingType == "ServerSetting")
            {
                errors.Add($"{label}: ServerSetting rows cannot be part of a feature.");
            }
            else if (row.SettingType is not ("Setting" or "SettingGroup"))
            {
                errors.Add($"{label} settingtype '{row.SettingType}' must be Setting or SettingGroup.");
            }

            if (row.Op == "set" && row.Value is null)
            {
                errors.Add($"{label} is a set row and needs a value (it may be an empty string).");
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in feature.Params)
        {
            var label = $"Parameter '{param.Name}'";

            if (!ParamNameRegex().IsMatch(param.Name ?? string.Empty))
            {
                errors.Add($"{label} has an invalid name; use letters, digits and underscore, starting with a letter.");
            }
            else if (FeaturePlaceholders.BuiltIns.Contains(param.Name!))
            {
                errors.Add($"{label} is a built-in placeholder and cannot be declared.");
            }

            if (!string.IsNullOrEmpty(param.Name) && !seen.Add(param.Name))
            {
                errors.Add($"{label} is declared more than once.");
            }

            if (!ParamTypes.Contains(param.Type))
            {
                errors.Add($"{label} type '{param.Type}' must be Text, Number, Boolean, List or Position.");
            }
            else if (param.Type == "List" && param.Values.Count == 0)
            {
                errors.Add($"{label} is a List and needs at least one value.");
            }
            else if (param.Type == "Position")
            {
                if (param.Default is not null)
                {
                    errors.Add($"{label} is a Position and cannot have a default; the consultant always answers it.");
                }

                if (param.Values.Count > 0)
                {
                    errors.Add($"{label} is a Position and cannot have list values; the answer is a column of the target table or first.");
                }
            }
        }

        ValidatePositionUse(feature, errors);
    }

    /// <summary>A Position parameter may only be the whole <c>after</c> of a <c>set</c> SettingGroup row.</summary>
    private static void ValidatePositionUse(SmFeature feature, List<string> errors)
    {
        var positions = new HashSet<string>(
            feature.Params.Where(p => p.Type == "Position" && !string.IsNullOrEmpty(p.Name)).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);
        if (positions.Count == 0)
        {
            return;
        }

        string Misuse(string where, string name) =>
            $"{where} uses Position parameter '{name}' outside a group's after; a Position parameter can only be the whole after of a set SettingGroup row (after: \"{{{{{name}}}}}\").";

        for (var i = 0; i < feature.Rows.Count; i++)
        {
            var row = feature.Rows[i];
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedAfter = IsGroupSetRow(row) && WholePlaceholder(row.After) is { } whole && positions.Contains(whole)
                ? whole
                : null;

            foreach (var text in new[] { row.Name, row.Value, row.ListValue, row.Layer })
            {
                foreach (var name in FeaturePlaceholders.Find(text).Where(positions.Contains))
                {
                    if (reported.Add(name))
                    {
                        errors.Add(Misuse($"Row {i + 1}", name));
                    }
                }
            }

            foreach (var name in FeaturePlaceholders.Find(row.After).Where(positions.Contains))
            {
                if (allowedAfter is null && reported.Add(name))
                {
                    errors.Add(Misuse($"Row {i + 1}", name));
                }
            }
        }

        for (var i = 0; i < feature.Steps.Count; i++)
        {
            var step = feature.Steps[i];
            var names = new HashSet<string>(FeaturePlaceholders.Find(step.Text), StringComparer.OrdinalIgnoreCase);
            names.UnionWith(FeaturePlaceholders.Find(step.Sql));
            foreach (var name in names.Where(positions.Contains))
            {
                errors.Add(Misuse($"Step {i + 1}", name));
            }
        }
    }

    /// <summary>
    /// Finish rule for group placement: every <c>set</c> SettingGroup row has an <c>after</c> that is a Position parameter
    /// or names an EARLIER <c>set</c> group row of the same feature (unsubstituted <c>NAME=VALUE</c>, or <c>NAME</c> for
    /// <c>Detail.*</c>): the plan places rows in feature row order, so a forward reference or a cycle could never resolve.
    /// Also used by the planner to refuse features finished before this rule existed.
    /// </summary>
    internal static IReadOnlyList<string> PositionErrors(SmFeature feature)
    {
        var errors = new List<string>();
        var positions = new HashSet<string>(
            feature.Params.Where(p => p.Type == "Position" && !string.IsNullOrEmpty(p.Name)).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < feature.Rows.Count; i++)
        {
            var row = feature.Rows[i];
            if (!IsGroupSetRow(row))
            {
                continue;
            }

            if (WholePlaceholder(row.After) is { } name && positions.Contains(name))
            {
                continue;
            }

            var after = row.After?.Trim();
            var chained = !string.IsNullOrEmpty(after) && feature.Rows
                .Take(i)
                .Where(IsGroupSetRow)
                .Any(other => string.Equals(GroupKey(other.Name, other.Value), after, StringComparison.OrdinalIgnoreCase));
            if (!chained)
            {
                errors.Add($"Row {i + 1} adds a group; ask the consultant where it goes (add a Position parameter as its after), or chain it after a group added in an earlier row.");
            }
        }

        return errors;
    }

    /// <summary>The anchor text of a group row: <c>NAME</c> for name-keyed <c>Detail.*</c> groups, else <c>NAME=VALUE</c>.</summary>
    internal static string GroupKey(string name, string? value) =>
        IsNameKeyedGroup(name) ? name : $"{name}={value}";

    internal static bool IsNameKeyedGroup(string name) => name.StartsWith("Detail.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The parameter name when <paramref name="text"/> is exactly one placeholder (surrounding whitespace ignored).</summary>
    internal static string? WholePlaceholder(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var match = WholePlaceholderRegex().Match(text.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsGroupSetRow(SmFeatureRow row) => row.Op == "set" && row.SettingType == "SettingGroup";

    private static void ValidateFinish(SmFeature feature, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(feature.Description))
        {
            errors.Add("A finished feature needs a description.");
        }

        if (feature.Rows.Count == 0 && feature.Steps.Count == 0)
        {
            errors.Add("A finished feature needs at least one row or step.");
        }

        var declared = new HashSet<string>(feature.Params.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in UsedPlaceholders(feature))
        {
            if (!FeaturePlaceholders.BuiltIns.Contains(name) && !declared.Contains(name))
            {
                errors.Add($"Placeholder '{{{{{name}}}}}' has no parameter.");
            }
        }

        foreach (var param in feature.Params)
        {
            var label = $"Parameter '{param.Name}'";

            if (string.IsNullOrWhiteSpace(param.Description))
            {
                errors.Add($"{label} needs a description.");
            }

            foreach (var value in param.Values.Where(v => string.IsNullOrWhiteSpace(v.Description)))
            {
                errors.Add($"{label} list value '{value.Value}' needs a description.");
            }

            if (param.Default is null)
            {
                continue;
            }

            var valid = param.Type switch
            {
                "Number" => decimal.TryParse(param.Default.Trim(), SmFeatureNumber.Style, CultureInfo.InvariantCulture, out _),
                "Boolean" => param.Default.Equals("True", StringComparison.OrdinalIgnoreCase)
                    || param.Default.Equals("False", StringComparison.OrdinalIgnoreCase),
                "List" => param.Values.Any(v => v.Value == param.Default),
                _ => true,
            };
            if (!valid)
            {
                errors.Add($"{label} default '{param.Default}' is not a valid {param.Type} value.");
            }
        }

        errors.AddRange(PositionErrors(feature));
    }

    private static void ReportUnusedParams(SmFeature feature, List<SmPlanWarning> warnings)
    {
        var used = UsedPlaceholders(feature);
        foreach (var param in feature.Params.Where(p => !string.IsNullOrEmpty(p.Name) && !used.Contains(p.Name)))
        {
            warnings.Add(new SmPlanWarning
            {
                Severity = "Warning",
                Code = "PARAM_UNUSED",
                Message = $"Parameter '{param.Name}' is not used by any row or step.",
            });
        }
    }

    private static HashSet<string> UsedPlaceholders(SmFeature feature)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in feature.Rows)
        {
            foreach (var text in new[] { row.Name, row.Value, row.ListValue, row.After, row.Layer })
            {
                used.UnionWith(FeaturePlaceholders.Find(text));
            }
        }

        foreach (var step in feature.Steps)
        {
            used.UnionWith(FeaturePlaceholders.Find(step.Text));
            used.UnionWith(FeaturePlaceholders.Find(step.Sql));
        }

        return used;
    }

    [GeneratedRegex("^(base|env:.+|lang:.+)$", RegexOptions.Singleline)]
    private static partial Regex LayerRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex ParamNameRegex();

    [GeneratedRegex(@"^\{\{([A-Za-z][A-Za-z0-9_]*)\}\}$")]
    private static partial Regex WholePlaceholderRegex();
}
