using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

// Value-keyed groups whose VALUE contains a dot (final review F6): the family of a value-keyed group is its NAME,
// a group row is never a child of a sibling with the same NAME, and a non-group row belongs to the longest
// enclosing group path. Sandbox table {p}DocumentListLayout plus Column=A 42, Column.A.Caption 44, Column=A.B 46,
// Column.A.B.Caption 48:
//   Column=DOCNR 10, Column.DOCNR.Caption 20, Column=DATE 30, Column.DATE.Caption 40, Column=A 42,
//   Column.A.Caption 44, Column=A.B 46, Column.A.B.Caption 48, Grid.PageSize 50, Grid.Secret (ServerSetting) 60.
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanGroupDottedValueTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task RemoveGroup_DoesNotRemoveSiblingWithDottedValue()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Remove("A"));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("Delete", i.Action));
        Assert.Single(result.Items, i => i.Name == "Column" && i.OldValue == "A");
        Assert.Single(result.Items, i => i.Name == "Column.A.Caption");
    }

    [Fact]
    public async Task RemoveGroupWithDottedValue_RemovesItsOwnBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Remove("A.B"));

        Assert.Equal(2, result.Items.Count);
        Assert.Single(result.Items, i => i.Name == "Column" && i.OldValue == "A.B");
        Assert.Single(result.Items, i => i.Name == "Column.A.B.Caption");
    }

    [Fact]
    public async Task RemoveGroup_PlusUpsertInSiblingBlock_Allowed()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Remove("A"), Row("Column.A.B.Width", "5"));

        Assert.Equal(2, result.Items.Count(i => i.Action == "Delete"));
        var insert = Assert.Single(result.Items, i => i.Action == "Insert");
        Assert.Equal("Column.A.B.Width", insert.Name);
        Assert.Equal(49, insert.OrderNr);
    }

    [Fact]
    public async Task NewGroup_AfterDottedSibling_PlacedAfterItsBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("X", after: "Column=A.B"));

        Assert.Equal(49, GroupOrderNr(result, "X"));
    }

    [Fact]
    public async Task NewGroup_AfterGroup_DottedSiblingIsNotPartOfItsBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("X", after: "Column=A"));

        Assert.Equal(45, GroupOrderNr(result, "X"));
    }

    [Fact]
    public async Task NewGroup_NoAfter_AfterDottedSiblingBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("X"));

        Assert.Equal(49, GroupOrderNr(result, "X"));
    }

    [Fact]
    public async Task NewGroup_First_BeforeFirstBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("X", after: "^"));

        Assert.Equal(5, GroupOrderNr(result, "X"));
    }

    [Fact]
    public async Task MoveDottedGroup_First_MovesItsBlockOnly()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Column", Value = "A.B", SettingType = "SettingGroup", Op = "move", After = "^" });

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(3, Assert.Single(result.Items, i => i.Name == "Column" && i.Value == "A.B").OrderNr);
        Assert.Equal(6, Assert.Single(result.Items, i => i.Name == "Column.A.B.Caption").OrderNr);
        Assert.DoesNotContain(result.Warnings, w => w.Severity == "Error");
    }

    [Fact]
    public async Task TidyTable_NoSplitGroup_AndRenumberKeepsOrder()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));
        var renumber = await fixture.Sm.CreateRenumberPlanAsync(Client, p, Table, "base", "renumber", session.Id, dryRun: true);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SPLIT_GROUP");
        Assert.Equal(TidyOrder, await FinalOrderAsync(p, renumber));
    }

    [Fact]
    public async Task Renumber_KeepsBothBlocks()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"UPDATE dbo.{p}{Table} SET ordernr = 5 WHERE NAME = N'Column.A.B.Caption';");

        var result = await fixture.Sm.CreateRenumberPlanAsync(Client, p, Table, "base", "renumber", session.Id, dryRun: true);

        Assert.Equal(TidyOrder, await FinalOrderAsync(p, result));
        Assert.DoesNotContain(result.Warnings, w => w.Code == "ORPHAN_ROWS");
    }

    [Fact]
    public async Task SplitDottedBlock_WarnsNamingOnlyThatGroup()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"UPDATE dbo.{p}{Table} SET ordernr = 5 WHERE NAME = N'Column.A.B.Caption';");

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var message = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP").Message;
        Assert.Contains("Column.A.B", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Column.A,", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Column.DOCNR", message, StringComparison.Ordinal);
    }

    private static readonly string[] TidyOrder =
    [
        "Column=DOCNR", "Column.DOCNR.Caption", "Column=DATE", "Column.DATE.Caption", "Column=A", "Column.A.Caption",
        "Column=A.B", "Column.A.B.Caption", "Grid.PageSize", "Grid.Secret",
    ];

    // Row keys of the table in the order after the plan's ordernr Updates.
    private async Task<string[]> FinalOrderAsync(string p, SmPlanResult result)
    {
        var order = (await PlanSandbox.RowsAsync(fixture, Client, p + Table))
            .ToDictionary(r => r.SettingType == "SettingGroup" ? r.Name + "=" + r.Value : r.Name, r => r.OrderNr);
        foreach (var item in result.Items)
        {
            order[item.SettingType == "SettingGroup" ? item.Name + "=" + item.Value : item.Name!] = item.OrderNr!.Value;
        }

        return order.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
    }

    private static int? GroupOrderNr(SmPlanResult result, string value) =>
        Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == "Column" && i.Value == value).OrderNr;

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "groups", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Column',             N'A',    N'SettingGroup', NULL, N'ALL', 42),
             (N'Column.A.Caption',   N'A',    N'Setting',      NULL, N'ALL', 44),
             (N'Column',             N'A.B',  N'SettingGroup', NULL, N'ALL', 46),
             (N'Column.A.B.Caption', N'A.B',  N'Setting',      NULL, N'ALL', 48);
            """);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string value) => new() { Table = Table, Name = name, Value = value };

    private static SmDesiredRow Group(string value, string? after = null) =>
        new() { Table = Table, Name = "Column", Value = value, SettingType = "SettingGroup", After = after };

    private static SmDesiredRow Remove(string value) =>
        new() { Table = Table, Name = "Column", Value = value, SettingType = "SettingGroup", Op = "remove" };
}
