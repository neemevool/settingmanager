using System.Globalization;

namespace VTMF.Settings.Core.Sm.Features;

/// <summary>A feature with all placeholders filled in: a merge desired state plus the substituted manual steps.</summary>
public sealed record SmFeatureExpansion(
    SmDesiredState Desired,
    IReadOnlyList<SmFeatureStep> Steps,
    IReadOnlyDictionary<string, string> Values);

/// <summary>Number syntax shared by default validation and answer parsing: optional sign and decimal point, no thousands separators.</summary>
internal static class SmFeatureNumber
{
    public const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
}

/// <summary>Turns a feature plus parameter answers into a merge desired state.</summary>
public static class SmFeatureExpander
{
    public static SmFeatureExpansion Expand(
        SmFeature feature,
        string process,
        string client,
        IReadOnlyDictionary<string, string>? answers)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["processname"] = process,
            ["clientname"] = client,
        };

        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (answers is not null)
        {
            var unknown = new List<string>();
            foreach (var pair in answers)
            {
                var known = FeaturePlaceholders.BuiltIns.Contains(pair.Key)
                    || feature.Params.Any(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (!known)
                {
                    unknown.Add(pair.Key);
                    continue;
                }

                // A null answer (explicit JSON null) counts as not answered.
                if (pair.Value is not null)
                {
                    given[pair.Key] = pair.Value;
                }
            }

            if (unknown.Count > 0)
            {
                var expected = feature.Params.Count == 0 ? "none" : string.Join(", ", feature.Params.Select(p => p.Name));
                throw new SmException(SmErrorCodes.FeatureParamInvalid,
                    string.Join("; ", unknown.Select(k => $"Unknown parameter '{k}'; expected: {expected}")));
            }
        }

        var missing = new List<string>();
        var invalid = new List<string>();
        foreach (var param in feature.Params)
        {
            if (!given.TryGetValue(param.Name, out var raw) && (raw = param.Default) is null)
            {
                missing.Add(param.Name);
                continue;
            }

            // A Position answer is a column name or "first"; blank means not answered (existence is checked by the plan).
            if (param.Type == "Position" && string.IsNullOrWhiteSpace(raw))
            {
                missing.Add(param.Name);
                continue;
            }

            var error = Normalize(param, raw, out var normalized);
            if (error is not null)
            {
                invalid.Add(error);
                continue;
            }

            values[param.Name] = normalized;
        }

        if (missing.Count > 0)
        {
            throw new SmException(SmErrorCodes.FeatureParamMissing, $"Missing values for: {string.Join(", ", missing)}");
        }

        if (invalid.Count > 0)
        {
            throw new SmException(SmErrorCodes.FeatureParamInvalid, string.Join("; ", invalid));
        }

        string Sub(string text) => FeaturePlaceholders.Substitute(text, values);
        string? SubOrNull(string? text) => text is null ? null : Sub(text);

        // An after that is exactly one Position placeholder becomes a group anchor: "^" for first, otherwise the
        // sibling group's key (NAME=answer, or family.answer for name-keyed Detail.* groups).
        string? PositionAfter(SmFeatureRow row)
        {
            var name = SmFeatureValidator.WholePlaceholder(row.After);
            var param = name is null
                ? null
                : feature.Params.FirstOrDefault(p => p.Type == "Position" && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (param is null)
            {
                return null;
            }

            var answer = values[param.Name];
            if (answer.Equals("first", StringComparison.OrdinalIgnoreCase))
            {
                return "^";
            }

            var groupName = Sub(row.Name);
            if (!SmFeatureValidator.IsNameKeyedGroup(groupName))
            {
                return $"{groupName}={answer}";
            }

            var family = groupName[..groupName.LastIndexOf('.')];
            return $"{family}.{answer}";
        }

        var rows = feature.Rows.Select(row => new SmDesiredRow
        {
            Table = row.Table,
            Layer = Sub(row.Layer),
            Name = Sub(row.Name),
            Value = SubOrNull(row.Value),
            SettingType = row.SettingType,
            ListValue = SubOrNull(row.ListValue),
            After = PositionAfter(row) ?? SubOrNull(row.After),
            Op = row.Op == "remove" ? "remove" : "upsert",
            OrderNr = null,
        }).ToList();

        var steps = feature.Steps.Select(step => new SmFeatureStep
        {
            Text = Sub(step.Text),
            Sql = SubOrNull(step.Sql),
        }).ToList();

        var desired = new SmDesiredState { Process = process, Mode = "merge", Scope = [], Rows = rows };
        return new SmFeatureExpansion(desired, steps, values);
    }

    /// <summary>Returns an error text, or null with the normalized value.</summary>
    private static string? Normalize(SmFeatureParam param, string raw, out string normalized)
    {
        normalized = raw;
        switch (param.Type)
        {
            case "Number":
                var text = raw.Trim();
                if (decimal.TryParse(text, SmFeatureNumber.Style, CultureInfo.InvariantCulture, out _))
                {
                    normalized = text;
                    return null;
                }

                return $"Parameter '{param.Name}' must be a number, got '{raw}'.";
            case "Boolean":
                if (bool.TryParse(raw.Trim(), out var flag))
                {
                    normalized = flag ? "True" : "False";
                    return null;
                }

                return $"Parameter '{param.Name}' must be true or false, got '{raw}'.";
            case "List":
                var match = param.Values.FirstOrDefault(v => string.Equals(v.Value, raw, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    normalized = match.Value;
                    return null;
                }

                return $"Parameter '{param.Name}' must be one of: {string.Join(", ", param.Values.Select(v => v.Value))}; got '{raw}'.";
            case "Position":
                normalized = raw.Trim();
                return null;
            default:
                return null;
        }
    }
}
