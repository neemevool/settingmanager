using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

// op move for SettingGroup blocks (spec 2026-10-24 setting groups, sections 2 and 4).
// Sandbox table {p}DocumentListLayout: Column=DOCNR 10, Column.DOCNR.Caption 20, Column=DATE 30,
// Column.DATE.Caption 40, Grid.PageSize 50, Grid.Secret (ServerSetting) 60.
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanGroupMoveTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task Move_First()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Move("DATE", "^"));

        Assert.Equal(2, result.Items.Count);
        AssertOrderUpdate(result, "Column", "DATE", 30, 3);
        AssertOrderUpdate(result, "Column.DATE.Caption", "Kuupäev", 40, 6);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "RENUMBER");
    }

    [Fact]
    public async Task Move_AfterSibling()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Move("DOCNR", "Column=DATE"));

        Assert.Equal(2, result.Items.Count);
        AssertOrderUpdate(result, "Column", "DOCNR", 10, 43);
        AssertOrderUpdate(result, "Column.DOCNR.Caption", "Dok nr", 20, 46);
    }

    [Fact]
    public async Task Move_ToOwnPosition_NoItems()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Move("DATE", "Column=DOCNR"));

        Assert.Empty(result.Items);
        Assert.Equal("NoChanges", result.Status);
    }

    [Fact]
    public async Task Move_AnchorInOwnBlock_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Move("DATE", "Column.DATE.Caption")));

        Assert.Equal(SmErrorCodes.GroupAnchorInvalid, ex.Code);
        Assert.Contains("Column.DATE.Caption", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_MissingGroup_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Move("NOPE", "^")));

        Assert.Equal(SmErrorCodes.GroupNotFound, ex.Code);
        Assert.Contains("NOPE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_MissingAnchor_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Move("DATE", "Column=NOPE")));

        Assert.Equal(SmErrorCodes.GroupAnchorNotFound, ex.Code);
        Assert.Contains("Column=NOPE", ex.Message, StringComparison.Ordinal);
        Assert.Contains(p + Table, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_PlusChildValueChange_OneItem()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Move("DATE", "^"), Row("Column.DATE.Caption", "Date2"));

        var caption = Assert.Single(result.Items, i => i.Name == "Column.DATE.Caption");
        Assert.Equal("Update", caption.Action);
        Assert.Equal("Date2", caption.Value);
        Assert.Equal(6, caption.OrderNr);
        Assert.Equal(40, caption.OldOrderNr);
        AssertOrderUpdate(result, "Column", "DATE", 30, 3);
        Assert.Equal(2, result.Items.Count);
    }

    // Name-keyed groups (Detail.*): Detail.Lines 70, Detail.Lines.Caption 80, Detail.Notes 90, Detail.Notes.Caption 100.
    [Fact]
    public async Task MoveNameKeyed_First()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);

        var result = await PlanAsync(p, session, MoveDetail("Detail.Notes", "^"));

        // Before the first Detail block (Detail.Lines at 70; the row before it is Grid.Secret at 60).
        Assert.Equal(2, result.Items.Count);
        AssertOrderUpdate(result, "Detail.Notes", "y", 90, 63);
        AssertOrderUpdate(result, "Detail.Notes.Caption", "Notes", 100, 66);
    }

    [Fact]
    public async Task MoveNameKeyed_AfterSibling()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);

        var result = await PlanAsync(p, session, MoveDetail("Detail.Lines", "Detail.Notes"));

        Assert.Equal(2, result.Items.Count);
        AssertOrderUpdate(result, "Detail.Lines", "x", 70, 110);
        AssertOrderUpdate(result, "Detail.Lines.Caption", "Lines", 80, 120);
    }

    [Fact]
    public async Task MoveNameKeyed_ToOwnPosition_NoItems()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);

        var result = await PlanAsync(p, session, MoveDetail("Detail.Notes", "Detail.Lines"));

        Assert.Equal("NoChanges", result.Status);
    }

    [Fact]
    public async Task MoveNameKeyed_AnchorOfOtherFamily_Refused()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, MoveDetail("Detail.Notes", "Column=DOCNR")));

        Assert.Equal(SmErrorCodes.GroupAnchorInvalid, ex.Code);
    }

    [Fact]
    public async Task Move_ThenInsertAnchoredOnMovedGroup()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Move("DATE", "^"),
            new SmDesiredRow { Table = Table, Name = "Column", Value = "X", SettingType = "SettingGroup", After = "Column=DATE" });

        var x = Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == "Column" && i.Value == "X");
        Assert.Equal(8, x.OrderNr);
    }

    [Fact]
    public async Task Move_ThenChildAnchoredOnMovedGroupRow_HonoursInBlockAnchor()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Move("DATE", "^"), Row("Column.DATE.Width", "1", after: "Column=DATE"));

        // DATE moves to 3 (caption 6); the in-block anchor puts Width between them: 3 + (6 - 3) / 2 = 4.
        var width = Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == "Column.DATE.Width");
        Assert.Equal(4, width.OrderNr);
        Assert.DoesNotContain(result.Warnings, w => w.Code is "ANCHOR_OUTSIDE_GROUP" or "RENUMBER");
    }

    [Fact]
    public async Task Move_First_ThenPlainRowAnchoredOnRowBefore_DoesNotJumpMovedBlock()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 1 WHERE NAME = N'Grid.PageSize';");

        var result = await PlanAsync(p, session, Move("DATE", "^"), Row("Zzz.X", "1", after: "Grid.PageSize"));

        // PageSize 1, DOCNR 10: DATE takes 1 + 9 / 3 = 4 and 7; Zzz.X goes right after PageSize: 1 + 3 / 2 = 2.
        AssertOrderUpdate(result, "Column", "DATE", 30, 4);
        var x = Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == "Zzz.X");
        Assert.Equal(2, x.OrderNr);
    }

    [Fact]
    public async Task Move_SameGroupTwice_InvalidDesired()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Move("DATE", "^"), Move("DATE", "Column=DOCNR")));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
        Assert.Contains("Column", ex.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_NonGroupRow_InvalidDesired()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Grid.PageSize", Value = "20", Op = "move", After = "^" }));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
        Assert.Contains("rows[0]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_ReplaceMode_InvalidDesired()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreatePlanAsync(Client, new SmDesiredState
        {
            Process = p,
            Mode = "replace",
            Scope = [new SmScopeEntry { Table = Table }],
            Rows = [Move("DATE", "^")],
        }, "groups", session.Id, dryRun: true));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
        Assert.Contains("move", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_ServerProcessSettings_InvalidDesired()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            new SmDesiredRow { Table = "ServerProcessSettings", Name = "Function", Value = "X", SettingType = "SettingGroup", Op = "move", After = "^" }));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
        Assert.Contains("ServerProcessSettings", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_ApplyThenRollback_RestoresOrder()
    {
        var (p, session) = await NewProcessAsync();
        var before = await OrderAsync(p);

        var plan = await fixture.Sm.CreatePlanAsync(Client,
            new SmDesiredState { Process = p, Rows = [Move("DOCNR", "Column=DATE")] }, "move", session.Id);
        Assert.Equal("Created", plan.Status);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!)).Status);
        var moved = await OrderAsync(p);
        Assert.Equal(43, moved["Column=DOCNR"]);
        Assert.Equal(46, moved["Column.DOCNR.Caption"]);

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);

        Assert.Equal(before, await OrderAsync(p));
    }

    private static void AssertOrderUpdate(SmPlanResult result, string name, string value, int oldOrderNr, int orderNr)
    {
        var item = Assert.Single(result.Items, i => i.Name == name && (i.Value ?? i.OldValue) == value);
        Assert.Equal("Update", item.Action);
        Assert.Equal(value, item.Value);
        Assert.Equal(value, item.OldValue);
        Assert.Equal(oldOrderNr, item.OldOrderNr);
        Assert.Equal(orderNr, item.OrderNr);
    }

    private async Task<Dictionary<string, int>> OrderAsync(string process) =>
        (await PlanSandbox.RowsAsync(fixture, Client, process + Table)).ToDictionary(
            r => r.SettingType == "SettingGroup" ? r.Name + "=" + r.Value : r.Name, r => r.OrderNr);

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "groups", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string value, string? after = null) =>
        new() { Table = Table, Name = name, Value = value, After = after };

    private Task AddDetailGroupsAsync(string p) =>
        PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Detail.Lines',         N'x',     N'SettingGroup', NULL, N'ALL', 70),
             (N'Detail.Lines.Caption', N'Lines', N'Setting',      NULL, N'ALL', 80),
             (N'Detail.Notes',         N'y',     N'SettingGroup', NULL, N'ALL', 90),
             (N'Detail.Notes.Caption', N'Notes', N'Setting',      NULL, N'ALL', 100);
            """);

    private static SmDesiredRow MoveDetail(string name, string after) =>
        new() { Table = Table, Name = name, Value = name == "Detail.Lines" ? "x" : "y", SettingType = "SettingGroup", Op = "move", After = after };

    private static SmDesiredRow Move(string value, string after) =>
        new() { Table = Table, Name = "Column", Value = value, SettingType = "SettingGroup", Op = "move", After = after };
}
