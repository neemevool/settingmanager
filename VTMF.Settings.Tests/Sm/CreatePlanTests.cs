using System.Text.RegularExpressions;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed partial class CreatePlanTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task Merge_UpdateValue_CreatesUpdateItem()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "page size", session.Id);

        Assert.Equal("Created", result.Status);
        Assert.NotNull(result.PlanId);
        Assert.Matches(Sha256Hex(), result.Checksum!);
        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("50", item.Value);
        Assert.Equal("20", item.OldValue);
        Assert.Equal(p + Table, item.PhysicalTable);
        Assert.Equal(Table, item.Table);
        Assert.Equal("base", item.Layer);
        Assert.Equal("ALL", item.WorkerCode);
        Assert.Equal(1, item.Seq);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", rows.Single(r => r.Name == "Grid.PageSize").Value);
    }

    [Fact]
    public async Task Merge_NewRowWithOrderNr_CreatesInsert()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.ShowFilterRow", "true", orderNr: 55)), "filter row", session.Id);

        var item = Assert.Single(result.Items);
        Assert.Equal("Insert", item.Action);
        Assert.Equal("Grid.ShowFilterRow", item.Name);
        Assert.Equal("true", item.Value);
        Assert.Equal("Setting", item.SettingType);
        Assert.Equal(55, item.OrderNr);
        Assert.Null(item.OldValue);
        Assert.Null(item.OldOrderNr);
    }

    [Fact]
    public async Task Merge_Remove_CreatesDelete()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Column.DATE.Caption", null, op: "remove")), "remove caption", session.Id);

        Assert.Equal("Created", result.Status);
        var item = Assert.Single(result.Items);
        Assert.Equal("Delete", item.Action);
        Assert.Equal("Column.DATE.Caption", item.Name);
        Assert.Null(item.Value);
        Assert.Equal("Kuupäev", item.OldValue);
        Assert.Equal("Setting", item.OldSettingType);
        Assert.Equal(40, item.OldOrderNr);
    }

    [Fact]
    public async Task Merge_RemoveMissing_WarnsRowNotFound()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("No.Such.Row", null, op: "remove")), "remove missing", session.Id);

        Assert.Equal("NoChanges", result.Status);
        Assert.Empty(result.Items);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("ROW_NOT_FOUND", warning.Code);
        Assert.Contains("No.Such.Row", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_CaseOnlyValueChange_CreatesUpdate()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Column.DATE.Caption", "KUUPÄEV")), "case", session.Id);

        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal("KUUPÄEV", item.Value);
        Assert.Equal("Kuupäev", item.OldValue);
    }

    [Fact]
    public async Task UnknownSession_ThrowsSessionNotFound()
    {
        var (p, _) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "no session", int.MaxValue));

        Assert.Equal(SmErrorCodes.SessionNotFound, ex.Code);
    }

    [Fact]
    public async Task CreateTableAndTargetAlias_ThrowsInvalidDesired()
    {
        var (p, session) = await NewProcessAsync();
        var options = new SmPlanOptions { CreateTable = true, TargetAlias = true };

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "options", session.Id, options));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
    }

    [Fact]
    public async Task Merge_SameValue_ReturnsNoChanges()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "20")), "same", session.Id);

        Assert.Equal("NoChanges", result.Status);
        Assert.Null(result.PlanId);
        Assert.Null(result.Checksum);
        Assert.Empty(result.Items);
        Assert.Empty(await fixture.Sm.GetPlansAsync(Client, p));
    }

    [Fact]
    public async Task Replace_DeletesRowsNotListed()
    {
        var (p, session) = await NewProcessAsync();
        var current = await fixture.Sm.GetSettingsAsync(Client, p, Table);
        var desired = new SmDesiredState
        {
            Process = p,
            Mode = "replace",
            Scope = [new SmScopeEntry { Table = Table, Layer = "base" }],
            Rows = current
                .Where(r => r.Name != "Grid.PageSize" && r.SettingType != "ServerSetting")
                .Select(r => new SmDesiredRow
                {
                    Table = Table, Layer = "base", Name = r.Name, Value = r.Value, SettingType = r.SettingType,
                    ListValue = r.ListValue, OrderNr = r.OrderNr,
                })
                .ToList(),
        };

        var result = await fixture.Sm.CreatePlanAsync(Client, desired, "replace", session.Id);

        var item = Assert.Single(result.Items);
        Assert.Equal("Delete", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("20", item.OldValue);
    }

    [Fact]
    public async Task Replace_RowWithoutOrderNr_ThrowsInvalidDesired()
    {
        var (p, session) = await NewProcessAsync();
        var desired = new SmDesiredState
        {
            Process = p,
            Mode = "replace",
            Scope = [new SmScopeEntry { Table = Table }],
            Rows = [Row("Grid.PageSize", "20")],
        };

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreatePlanAsync(Client, desired, "replace", session.Id));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
    }

    [Fact]
    public async Task InvalidJsonMode_ThrowsInvalidDesired()
    {
        var (p, session) = await NewProcessAsync();
        var desired = new SmDesiredState { Process = p, Mode = "bogus", Rows = [Row("Grid.PageSize", "50")] };

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreatePlanAsync(Client, desired, "bad mode", session.Id));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
    }

    [Fact]
    public async Task WorkerLayer_ThrowsWorkerLayer()
    {
        var (p, session) = await NewProcessAsync();
        var row = new SmDesiredRow { Table = Table, Layer = "worker:bob", Name = "Grid.PageSize", Value = "50" };

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreatePlanAsync(Client, Merge(p, row), "worker", session.Id));

        Assert.Equal(SmErrorCodes.WorkerLayer, ex.Code);
        Assert.Contains("env:{environment}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosedSession_ThrowsSessionClosed()
    {
        var (p, session) = await NewProcessAsync();
        await fixture.Sm.CloseSessionAsync(Client, session.Id);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "closed", session.Id));

        Assert.Equal(SmErrorCodes.SessionClosed, ex.Code);
    }

    [Fact]
    public async Task ServerProcessSettings_PrefixesName()
    {
        var (p, session) = await NewProcessAsync();
        var row = new SmDesiredRow { Table = "ServerProcessSettings", Name = "Function.Barcode.Parser", Value = "X" };

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, row), "parser", session.Id);

        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal(p + ".Function.Barcode.Parser", item.Name);
        Assert.Equal("ServerProcessSettings", item.PhysicalTable);
        Assert.Equal("X", item.Value);
        Assert.Equal("Parserbuilder", item.OldValue);
    }

    [Fact]
    public async Task DryRun_ReturnsItemsWithoutStoring()
    {
        var (p, session) = await NewProcessAsync();

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "dry", session.Id, dryRun: true);

        Assert.Equal("DryRun", result.Status);
        Assert.Null(result.PlanId);
        Assert.Null(result.Checksum);
        Assert.NotEmpty(result.Items);
        Assert.Empty(await fixture.Sm.GetPlansAsync(Client, p));
    }

    [Fact]
    public async Task GetPlan_ReturnsStoredItemsAndChecksumMatchesHeader()
    {
        var (p, session) = await NewProcessAsync();
        var desired = Merge(p, Row("Grid.PageSize", "50"), Row("Grid.ShowFilterRow", "true", orderNr: 55), Row("Column.DATE.Caption", null, op: "remove"));

        var result = await fixture.Sm.CreatePlanAsync(Client, desired, "three changes", session.Id, source: "test");
        var details = await fixture.Sm.GetPlanAsync(Client, result.PlanId!.Value);
        var plans = await fixture.Sm.GetPlansAsync(Client, p);
        var sessionAfter = (await fixture.Sm.GetSessionsAsync(Client, p)).Single();

        Assert.Equal(result.PlanId, details.Plan.Id);
        Assert.Equal(result.Checksum, details.Plan.Checksum);
        Assert.Equal("Created", details.Plan.Status);
        Assert.Equal(session.Id, details.Plan.SessionId);
        Assert.Equal(p, details.Plan.Process);
        Assert.Equal("merge", details.Plan.Mode);
        Assert.Equal("test", details.Plan.Source);
        Assert.Equal("three changes", details.Plan.Description);
        Assert.False(string.IsNullOrEmpty(details.Plan.CreatedBy));
        Assert.Null(details.Plan.AppliedAt);
        Assert.Equal(
            result.Items.Select(i => (i.Seq, i.Action, i.Name, i.Value, i.OldValue, i.OrderNr, i.OldOrderNr)),
            details.Items.Select(i => (i.Seq, i.Action, i.Name, i.Value, i.OldValue, i.OrderNr, i.OldOrderNr)));
        Assert.Equal(["Delete", "Update", "Insert"], details.Items.Select(i => i.Action).ToArray());
        Assert.Equal([1, 2, 3], details.Items.Select(i => i.Seq).ToArray());
        Assert.Equal(result.PlanId, Assert.Single(plans).Id);
        Assert.Equal(1, sessionAfter.PlanCount);
        Assert.True(sessionAfter.LastActivity >= session.LastActivity);
    }

    [Fact]
    public async Task GetPlan_Unknown_ThrowsPlanNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetPlanAsync(Client, int.MaxValue));

        Assert.Equal(SmErrorCodes.PlanNotFound, ex.Code);
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string? value, int? orderNr = null, string op = "upsert") =>
        new() { Table = Table, Name = name, Value = value, OrderNr = orderNr, Op = op };

    private static SmDesiredState Merge(string process, params SmDesiredRow[] rows) =>
        new() { Process = process, Rows = rows };

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
