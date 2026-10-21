namespace VTMF.Settings.Core.Sm.Features;

/// <summary>Plans a finished feature for one process and checks that the process code actually reads the server settings it sets.</summary>
public sealed class SmFeaturePlanner(SmClient sm, SmFeatureStore features)
{
    private const string ServerTable = "ServerProcessSettings";

    public async Task<SmFeaturePlanResult> FeaturePlanAsync(
        string client,
        int featureId,
        string process,
        int sessionId,
        IReadOnlyDictionary<string, string>? parameters,
        SmPlanOptions? options = null,
        CancellationToken ct = default)
    {
        var feature = await features.GetFeatureAsync(featureId, null, ct).ConfigureAwait(false);
        if (!string.Equals(feature.Status, "Finished", StringComparison.Ordinal))
        {
            throw new SmException(
                SmErrorCodes.FeatureNotFinished,
                $"Feature '{feature.Name}' is a draft; only finished features can be applied.");
        }

        var expansion = SmFeatureExpander.Expand(feature, process, client, parameters);
        var plan = await sm.CreatePlanAsync(
            client, expansion.Desired, $"Feature {feature.Name}", sessionId, options,
            source: $"feature:{featureId}:{feature.Name}", cancellationToken: ct).ConfigureAwait(false);

        var warnings = new List<SmPlanWarning>();
        var serverNames = expansion.Desired.Rows
            .Where(r => r.Op == "upsert" && string.Equals(r.Table, ServerTable, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (serverNames.Count > 0)
        {
            var usage = await sm.GetSettingsUsageAsync(client, process, cancellationToken: ct).ConfigureAwait(false);
            var uncertain = usage.Any(u => string.Equals(u.CallStyle, "unparsed", StringComparison.Ordinal));
            foreach (var name in serverNames)
            {
                var read = usage.Any(u =>
                    u.ProcedureName is not null && !u.Unused
                    && string.Equals(u.SettingName, name, StringComparison.OrdinalIgnoreCase));
                if (read)
                {
                    continue;
                }

                var message = $"'{name}' is not read by any {process} procedure; the code of {process} may not support this feature.";
                if (uncertain)
                {
                    message += " Some procedure reads could not be parsed, so this is uncertain.";
                }

                warnings.Add(new SmPlanWarning { Severity = "Warning", Code = "SETTING_NOT_READ", Message = message });
            }
        }

        return new SmFeaturePlanResult
        {
            FeatureId = featureId,
            FeatureName = feature.Name,
            Plan = plan,
            Warnings = warnings,
            Steps = expansion.Steps,
            Values = expansion.Values,
        };
    }
}
