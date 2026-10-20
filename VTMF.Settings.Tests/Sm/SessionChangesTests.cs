using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SessionChangesTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";
    private const string NewTable = "DocumentNewLayout";

    [Fact]
    public async Task NetChange_RevertedValueDropsOut()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "20"));
        await ApplyAsync(p, session, null, Row("Column.DATE.Caption", "Date"));

        var changes = await fixture.Sm.GetSessionChangesAsync(Client, session.Id);

        var item = Assert.Single(changes);
        Assert.Equal(1, item.Seq);
        Assert.Equal("Update", item.Action);
        Assert.Equal(p + Table, item.PhysicalTable);
        Assert.Equal("Column.DATE.Caption", item.Name);
        Assert.Equal("Kuupäev", item.OldValue);
        Assert.Equal("Date", item.Value);
    }

    [Fact]
    public async Task NetChange_ValueChangedThreeTimes_ShowsFirstAndLast()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "30"));
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "40"));
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));

        var changes = await fixture.Sm.GetSessionChangesAsync(Client, session.Id);

        var item = Assert.Single(changes);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("20", item.OldValue);
        Assert.Equal("50", item.Value);
        Assert.Equal(50, item.OldOrderNr);
        Assert.Equal(50, item.OrderNr);
    }

    [Fact]
    public async Task NetChange_InsertThenUpdate_IsInsertWithFinalValue()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, null, Row("Grid.ShowFilterRow", "true"));
        await ApplyAsync(p, session, null, Row("Grid.ShowFilterRow", "false"));

        var changes = await fixture.Sm.GetSessionChangesAsync(Client, session.Id);

        var item = Assert.Single(changes);
        Assert.Equal("Insert", item.Action);
        Assert.Equal("Grid.ShowFilterRow", item.Name);
        Assert.Equal("false", item.Value);
        Assert.Equal("Setting", item.SettingType);
        Assert.Null(item.OldValue);
        Assert.Null(item.OldSettingType);
        Assert.Null(item.OldOrderNr);
    }

    [Fact]
    public async Task NetChange_IncludesCreatedTable()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        await ApplyAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "10", table: NewTable));

        var changes = await fixture.Sm.GetSessionChangesAsync(Client, session.Id);

        Assert.Equal(["CreateTable", "Update", "Insert"], changes.Select(i => i.Action).ToArray());
        Assert.Equal([1, 2, 3], changes.Select(i => i.Seq).ToArray());
        Assert.Equal(p + NewTable, changes[0].PhysicalTable);
        Assert.Null(changes[0].Name);
        Assert.Equal(p + Table, changes[1].PhysicalTable);
        Assert.Equal(p + NewTable, changes[2].PhysicalTable);
        Assert.Equal("10", changes[2].Value);
    }

    [Fact]
    public async Task NetChange_TableCreatedAndRolledBack_DropsOut()
    {
        var (p, session) = await NewProcessAsync();
        var created = await ApplyAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "10", table: NewTable));
        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, created);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);

        Assert.Empty(await fixture.Sm.GetSessionChangesAsync(Client, session.Id));
        Assert.Equal("NoChanges", (await fixture.Sm.CreateSessionRollbackPlanAsync(Client, session.Id)).Status);
    }

    [Fact]
    public async Task GetSessionChanges_UnknownSession_ThrowsSessionNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetSessionChangesAsync(Client, int.MaxValue));

        Assert.Equal(SmErrorCodes.SessionNotFound, ex.Code);
    }

    [Fact]
    public async Task SessionRollback_RestoresStartState()
    {
        var (p, session) = await NewProcessAsync();
        var start = Snapshot(await PlanSandbox.RowsAsync(fixture, Client, p + Table));
        var plans = new[]
        {
            await ApplyAsync(p, session, null, Row("Grid.PageSize", "50")),
            await ApplyAsync(p, session, null, Row("Grid.ShowFilterRow", "true")),
            await ApplyAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "10", table: NewTable)),
        };

        var rollback = await fixture.Sm.CreateSessionRollbackPlanAsync(Client, session.Id);

        Assert.Equal("Created", rollback.Status);
        Assert.Equal(["BackupTable", "Delete", "Delete", "Update", "DropTable"], rollback.Items.Select(i => i.Action).ToArray());
        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);
        Assert.Equal("Applied", result.Status);
        Assert.Equal(start, Snapshot(await PlanSandbox.RowsAsync(fixture, Client, p + Table)));
        Assert.Null(await fixture.ScalarOnAsync<int?>(Client, $"SELECT OBJECT_ID(N'dbo.{p}{NewTable}', N'U')"));
        foreach (var plan in plans)
        {
            Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, plan)).Plan.Status);
        }

        var stored = (await fixture.Sm.GetPlanAsync(Client, rollback.PlanId.Value)).Plan;
        Assert.Null(stored.RollbackOf);
        Assert.Equal(session.Id, stored.SessionId);
        Assert.Equal(p, stored.Process);
        Assert.Equal($"rollback-session:{session.Id}", stored.Source);
        Assert.Equal($"Rollback of session {session.Id}: test", stored.Description);
        Assert.Equal("Applied", stored.Status);
        Assert.Empty(await fixture.Sm.GetSessionChangesAsync(Client, session.Id));
    }

    [Fact]
    public async Task SessionRollback_NothingChanged_ReturnsNoChanges()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        await ApplyAsync(p, session, null, Row("Grid.PageSize", "20"));

        var rollback = await fixture.Sm.CreateSessionRollbackPlanAsync(Client, session.Id);

        Assert.Equal("NoChanges", rollback.Status);
        Assert.Null(rollback.PlanId);
        Assert.Null(rollback.Checksum);
        Assert.Empty(rollback.Items);
        Assert.Equal(0, await fixture.ScalarOnAsync<int>(Client, $"SELECT COUNT(*) FROM sm.[plan] WHERE source = N'rollback-session:{session.Id}'"));
    }

    [Fact]
    public async Task SessionRollback_AfterUiChange_IsStale()
    {
        var (p, session) = await NewProcessAsync();
        var first = await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        var second = await ApplyAsync(p, session, null, Row("Column.DATE.Caption", "Date"));
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET VALUE = N'60' WHERE NAME = N'Grid.PageSize';");
        var rollback = await fixture.Sm.CreateSessionRollbackPlanAsync(Client, session.Id);

        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Grid.PageSize", conflict.Name);
        Assert.Equal("50", conflict.ExpectedValue);
        Assert.Equal("60", conflict.CurrentValue);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("Date", Assert.Single(rows, r => r.Name == "Column.DATE.Caption").Value);
        Assert.Equal("60", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, first)).Plan.Status);
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, second)).Plan.Status);
    }

    private static (string Name, string Value, string SettingType, string? ListValue, int OrderNr)[] Snapshot(IReadOnlyList<SmSettingRow> rows) =>
        [.. rows.Select(r => (r.Name, r.Value, r.SettingType, r.ListValue, r.OrderNr))];

    private async Task<int> ApplyAsync(string process, SmSession session, SmPlanOptions? options, params SmDesiredRow[] rows)
    {
        var plan = await fixture.Sm.CreatePlanAsync(Client, Desired(process, rows), "apply", session.Id, options);
        Assert.Equal("Created", plan.Status);
        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);
        Assert.Equal("Applied", result.Status);
        return plan.PlanId.Value;
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredState Desired(string process, params SmDesiredRow[] rows) => new() { Process = process, Rows = rows };

    private static SmDesiredRow Row(string name, string value, string table = Table) =>
        new() { Table = table, Name = name, Value = value };
}
