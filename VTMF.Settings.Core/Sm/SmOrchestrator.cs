namespace VTMF.Settings.Core.Sm;

/// <summary>Cross-client operations built on <see cref="SmClient"/>: compare and clone a process between two clients.</summary>
public sealed class SmOrchestrator(SmClient sm)
{
    private const string ServerSettingType = "ServerSetting";
    private const string ServerProcessSettings = "ServerProcessSettings";

    /// <summary>Dry-run plan against <paramref name="target"/> showing what differs from <paramref name="source"/>; nothing is stored.</summary>
    public async Task<SmPlanResult> CompareClientsAsync(
        string source, string target, string process, IReadOnlyList<SmScopeEntry>? scope = null,
        CancellationToken cancellationToken = default)
    {
        var desired = await BuildDesiredAsync(source, process, scope, cancellationToken).ConfigureAwait(false);
        return await sm.CreatePlanAsync(
            target, desired, $"Compare with {source}", sessionId: null,
            options: new SmPlanOptions { CreateTable = true }, source: $"clone:{source}", dryRun: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stores a plan on <paramref name="target"/> that makes the process equal to <paramref name="source"/>.</summary>
    public async Task<SmPlanResult> ClonePlanAsync(
        string source, string target, string process, int sessionId, IReadOnlyList<SmScopeEntry>? scope = null,
        SmPlanOptions? options = null, CancellationToken cancellationToken = default)
    {
        var desired = await BuildDesiredAsync(source, process, scope, cancellationToken).ConfigureAwait(false);
        return await sm.CreatePlanAsync(
            target, desired, $"Clone {process} from {source}", sessionId, options, source: $"clone:{source}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<SmDesiredState> BuildDesiredAsync(
        string source, string process, IReadOnlyList<SmScopeEntry>? scope, CancellationToken cancellationToken)
    {
        var entries = scope ?? await DefaultScopeAsync(source, process, cancellationToken).ConfigureAwait(false);
        var rows = new List<SmDesiredRow>();
        foreach (var entry in entries)
        {
            var settings = await sm.GetSettingsAsync(source, process, entry.Table, entry.Layer, cancellationToken: cancellationToken).ConfigureAwait(false);
            rows.AddRange(settings
                .Where(s => !string.Equals(s.SettingType, ServerSettingType, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SmDesiredRow
                {
                    Table = entry.Table,
                    Layer = entry.Layer,
                    Name = s.Name,
                    Value = s.Value,
                    SettingType = s.SettingType,
                    ListValue = s.ListValue,
                    OrderNr = s.OrderNr,
                }));
        }

        return new SmDesiredState { Process = process, Mode = "replace", Scope = entries, Rows = rows };
    }

    private async Task<IReadOnlyList<SmScopeEntry>> DefaultScopeAsync(string source, string process, CancellationToken cancellationToken)
    {
        var tables = await sm.GetTablesAsync(source, process, cancellationToken).ConfigureAwait(false);
        var scope = tables
            .Where(t => string.Equals(t.Owner, process, StringComparison.OrdinalIgnoreCase))
            .Select(t => new SmScopeEntry { Table = t.View, Layer = t.Layer })
            .ToList();
        if (!scope.Any(e => e.Table == ServerProcessSettings && e.Layer == "base"))
        {
            scope.Add(new SmScopeEntry { Table = ServerProcessSettings, Layer = "base" });
        }

        return scope;
    }
}
