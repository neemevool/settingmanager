using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

// Block-aware placement of new rows (spec 2026-10-24 setting groups, sections 2 and 3).
// Sandbox table {p}DocumentListLayout: Column=DOCNR 10, Column.DOCNR.Caption 20, Column=DATE 30,
// Column.DATE.Caption 40, Grid.PageSize 50, Grid.Secret (ServerSetting) 60.
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanGroupPlacementTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task NewGroup_AfterGroup_PlacedAfterWholeBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Group("BARCODE", after: "Column=DOCNR"),
            Row("Column.BARCODE.Caption", "Barcode"));

        Assert.Equal(23, GroupOrderNr(result, "BARCODE"));
        Assert.Equal(26, OrderNr(result, "Column.BARCODE.Caption"));
        Assert.DoesNotContain(result.Warnings, w => w.Code == "RENUMBER");
    }

    [Fact]
    public async Task NewGroup_ChildrenListedFirst_SameResult()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Row("Column.BARCODE.Caption", "Barcode"),
            Group("BARCODE", after: "Column=DOCNR"));

        Assert.Equal(23, GroupOrderNr(result, "BARCODE"));
        Assert.Equal(26, OrderNr(result, "Column.BARCODE.Caption"));
    }

    [Fact]
    public async Task NewGroup_First_BeforeFirstBlockOfFamily()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Group("BARCODE", after: "^"),
            Row("Column.BARCODE.Caption", "Barcode"));

        Assert.Equal(3, GroupOrderNr(result, "BARCODE"));
        Assert.Equal(6, OrderNr(result, "Column.BARCODE.Caption"));
    }

    [Fact]
    public async Task NewGroup_NoAfter_AfterLastBlockOfFamily()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Group("BARCODE"),
            Row("Column.BARCODE.Caption", "Barcode"));

        Assert.Equal(43, GroupOrderNr(result, "BARCODE"));
        Assert.Equal(46, OrderNr(result, "Column.BARCODE.Caption"));
    }

    [Fact]
    public async Task NewGroup_AnchorMissing_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            PlanAsync(p, session, Group("BARCODE", after: "Column=NOPE"), Row("Column.BARCODE.Caption", "Barcode")));

        Assert.Equal(SmErrorCodes.GroupAnchorNotFound, ex.Code);
        Assert.Contains("NOPE", ex.Message, StringComparison.Ordinal);
        Assert.Contains(p + Table, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewGroup_AnchorNotAGroup_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            PlanAsync(p, session, Group("BARCODE", after: "Grid.PageSize")));

        Assert.Equal(SmErrorCodes.GroupAnchorInvalid, ex.Code);
        Assert.Contains("Grid.PageSize", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewGroup_AnchorDifferentCase_Found()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("BARCODE", after: "Column=docnr"));

        Assert.Equal(25, GroupOrderNr(result, "BARCODE"));
    }

    [Fact]
    public async Task TwoNewGroups_Chained()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Group("A", after: "Column=DOCNR"),
            Group("B", after: "Column=A"));

        Assert.Equal(25, GroupOrderNr(result, "A"));
        Assert.Equal(27, GroupOrderNr(result, "B"));
    }

    [Fact]
    public async Task PlainRow_AnchorOnGroupRow_PlacedAfterBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Zzz.X", "1", after: "Column=DOCNR"));

        Assert.Equal(25, OrderNr(result, "Zzz.X"));
    }

    // Longest common prefix of Grid.X is Grid: its last row Grid.Secret (60) lies inside the split DATE block
    // (30, 40, Column.DATE.Width 70), so the row goes after that block's end.
    [Fact]
    public async Task PlainRow_LongestPrefixInsideBlock_PlacedAfterBlock()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Column.DATE.Width', N'5', N'Setting', NULL, N'ALL', 70);");

        var result = await PlanAsync(p, session, Row("Grid.X", "1"));

        Assert.Equal(80, OrderNr(result, "Grid.X"));
        Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    // G11: anchors resolve only within the table of the row being placed; Column=DOCNR exists only in base.
    [Fact]
    public async Task EnvGroup_AnchorOnlyInBase_Refused()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "wt2", "wt2",
            ("Column", "ENV1", "SettingGroup", 10),
            ("Column.ENV1.Caption", "Env", "Setting", 20));

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Layer = "env:wt2", Name = "Column", Value = "X", SettingType = "SettingGroup", After = "Column=DOCNR" }));

        Assert.Equal(SmErrorCodes.GroupAnchorNotFound, ex.Code);
        Assert.Contains("Column=DOCNR", ex.Message, StringComparison.Ordinal);
        Assert.Contains(p + Table + "wt2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvGroup_AnchorInEnvTable_PlacedThere()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "wt2", "wt2",
            ("Column", "ENV1", "SettingGroup", 10),
            ("Column.ENV1.Caption", "Env", "Setting", 20));

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Layer = "env:wt2", Name = "Column", Value = "X", SettingType = "SettingGroup", After = "Column=ENV1" });

        var item = Assert.Single(result.Items);
        Assert.Equal(p + Table + "wt2", item.PhysicalTable);
        Assert.Equal(30, item.OrderNr);
    }

    [Fact]
    public async Task Child_ForeignAnchor_WarnsAndGoesToOwnBlockEnd()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DATE.Width", "1", after: "Column.DOCNR.Caption"));

        Assert.Equal(45, OrderNr(result, "Column.DATE.Width"));
        var warning = Assert.Single(result.Warnings, w => w.Code == "ANCHOR_OUTSIDE_GROUP");
        Assert.Contains("Column.DOCNR.Caption", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Child_InBlockAnchor_Honoured()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DATE.Width", "1", after: "Column=DATE"));

        Assert.Equal(35, OrderNr(result, "Column.DATE.Width"));
        Assert.DoesNotContain(result.Warnings, w => w.Code == "ANCHOR_OUTSIDE_GROUP");
    }

    [Fact]
    public async Task Caret_OnPlainRow_InvalidDesired()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Row("Zzz.X", "1", after: "^")));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
    }

    [Fact]
    public async Task ExistingGroup_WithAfter_NotMoved()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Group("DATE", after: "Column=NOPE"));

        Assert.Empty(result.Items);
        var warning = Assert.Single(result.Warnings, w => w.Code == "GROUP_NOT_MOVED");
        Assert.Contains("DATE", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupRow_ExplicitOrderNr_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Group("DATE", orderNr: 5)));

        Assert.Equal(SmErrorCodes.GroupOrderNr, ex.Code);
    }

    [Fact]
    public async Task NewGroupRow_ExplicitOrderNr_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, Group("BARCODE", orderNr: 25)));

        Assert.Equal(SmErrorCodes.GroupOrderNr, ex.Code);
        Assert.Contains("BARCODE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewGroup_GapTooSmall_Renumbers()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 28 WHERE NAME = N'Column.DOCNR.Caption';");

        var result = await PlanAsync(p, session,
            Group("BARCODE", after: "Column=DOCNR"),
            Row("Column.BARCODE.Caption", "Barcode"));

        Assert.Single(result.Warnings, w => w.Code == "RENUMBER");
        Assert.Equal(30, GroupOrderNr(result, "BARCODE"));
        Assert.Equal(40, OrderNr(result, "Column.BARCODE.Caption"));
        var date = Assert.Single(result.Items, i => i.Action == "Update" && i.Name == "Column" && i.Value == "DATE");
        Assert.Equal(30, date.OldOrderNr);
        Assert.Equal(50, date.OrderNr);
    }

    [Fact]
    public async Task NewGroup_NextToTiedRows_DistinctOrderNr()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 50 WHERE NAME = N'Column.DATE.Caption';");

        var result = await PlanAsync(p, session, Group("X"));

        Assert.Single(result.Warnings, w => w.Code == "RENUMBER");
        var final = await FinalRowsAsync(p, result);
        var x = final.Single(r => r.Name == "Column" && r.Value == "X").OrderNr;
        Assert.True(x > final.Single(r => r.Name == "Column.DATE.Caption").OrderNr, $"X={x}");
        Assert.True(x < final.Single(r => r.Name == "Grid.PageSize").OrderNr, $"X={x}");
        Assert.Equal(final.Count, final.Select(r => r.OrderNr).Distinct().Count());
    }

    [Fact]
    public async Task NewGroup_AllOrderNrZero_Renumbers()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 0;");

        var result = await PlanAsync(p, session, Group("BARCODE", after: "Column=DOCNR"));

        Assert.Single(result.Warnings, w => w.Code == "RENUMBER");
        var final = await FinalRowsAsync(p, result);
        Assert.Equal(final.Count, final.Select(r => r.OrderNr).Distinct().Count());
        // With every ordernr 0 the current order is the sort key (ordernr, NAME, VALUE): the DOCNR block ends at
        // Column.DOCNR.Caption, so BARCODE goes right behind it; the other rows keep their relative order.
        Assert.Equal(
            [
                "Column=DATE", "Column=DOCNR", "Column.DATE.Caption", "Column.DOCNR.Caption", "Column=BARCODE",
                "Grid.PageSize", "Grid.Secret",
            ],
            final.OrderBy(r => r.OrderNr).Select(r => r.Key).ToArray());
    }

    [Fact]
    public async Task NameKeyedGroup_AfterGroup_PlacedAfterBlock()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Detail.Lines',         N'x',     N'SettingGroup', NULL, N'ALL', 70),
             (N'Detail.Lines.Caption', N'Lines', N'Setting',      NULL, N'ALL', 80);
            """);

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Detail.Notes", Value = "y", SettingType = "SettingGroup", After = "Detail.Lines" },
            Row("Detail.Notes.Caption", "Notes"));

        Assert.Equal(90, OrderNr(result, "Detail.Notes"));
        Assert.Equal(100, OrderNr(result, "Detail.Notes.Caption"));
    }

    [Fact]
    public async Task SplitTable_Touched_WarnsSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"UPDATE dbo.{p}{Table} SET ordernr = 45 WHERE NAME = N'Column.DOCNR.Caption';");

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var warning = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP");
        Assert.Contains("DOCNR", warning.Message, StringComparison.Ordinal);
        Assert.Contains("renumber", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TiedSiblingGroups_Touched_WarnsSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"UPDATE dbo.{p}{Table} SET ordernr = 10 WHERE NAME = N'Column' AND VALUE = N'DATE';");

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var message = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP").Message;
        Assert.Contains("Column.DATE", message, StringComparison.Ordinal);
        Assert.Contains("Column.DOCNR", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TidyTable_NoSplitGroup()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    [Fact]
    public async Task ChildBeforeGroupRow_WarnsNamingThatGroup()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderNrAsync(p, "NAME = N'Column.DATE.Caption'", 5);

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var message = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP").Message;
        Assert.Contains("Column.DATE", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Column.DOCNR", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignRowInsideBlock_WarnsNamingThatGroup()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderNrAsync(p, "NAME = N'Grid.PageSize'", 15);

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var message = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP").Message;
        Assert.Contains("Column.DOCNR", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Column.DATE", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NameKeyedGroupSplit_WarnsNamingDetailGroup()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Detail.Lines',         N'x',     N'SettingGroup', NULL, N'ALL', 70),
             (N'Zzz.Y',                N'1',     N'Setting',      NULL, N'ALL', 80),
             (N'Detail.Lines.Caption', N'Lines', N'Setting',      NULL, N'ALL', 95);
            """);

        var result = await PlanAsync(p, session, Row("Zzz.X", "1"));

        var message = Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP").Message;
        Assert.Contains("Detail.Lines", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Column.DOCNR", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SplitTable_MoveOnly_WarnsSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderNrAsync(p, "NAME = N'Column.DOCNR.Caption'", 45);

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Column", Value = "DATE", SettingType = "SettingGroup", Op = "move", After = "^" });

        Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    [Fact]
    public async Task SplitTable_DeleteOnly_WarnsSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderNrAsync(p, "NAME = N'Column.DOCNR.Caption'", 45);

        var result = await PlanAsync(p, session, new SmDesiredRow { Table = Table, Name = "Grid.PageSize", Op = "remove" });

        Assert.All(result.Items, i => Assert.Equal("Delete", i.Action));
        Assert.Single(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    [Fact]
    public async Task SplitTable_ValueOnlyUpdate_NoSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderNrAsync(p, "NAME = N'Column.DOCNR.Caption'", 45);

        var result = await PlanAsync(p, session, Row("Grid.PageSize", "99"));

        Assert.Single(result.Items, i => i.Action == "Update" && i.Name == "Grid.PageSize");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    [Fact]
    public async Task LangTable_NoGroupRows_NoSplitGroup()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "en", "ALL",
            ("Column.DOCNR.Caption", "Doc no", "Setting", 10),
            ("Column.DATE.Caption", "Date", "Setting", 20));

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Column", Value = "DOCNR", SettingType = "SettingGroup", Op = "remove" });

        Assert.Contains(result.Items, i => i.PhysicalTable == p + Table + "en" && i.Action == "Delete");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "SPLIT_GROUP");
    }

    private Task SetOrderNrAsync(string p, string where, int orderNr) =>
        PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = {orderNr} WHERE {where};");

    private static int? OrderNr(SmPlanResult result, string name) =>
        Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == name).OrderNr;

    private static int? GroupOrderNr(SmPlanResult result, string value) =>
        Assert.Single(result.Items, i => i.Action == "Insert" && i.Name == "Column" && i.Value == value).OrderNr;

    // The table as it will be after the plan: current rows with Update/Delete items applied, plus Inserts.
    private async Task<List<FinalRow>> FinalRowsAsync(string process, SmPlanResult result)
    {
        var current = await PlanSandbox.RowsAsync(fixture, Client, process + Table);
        var rows = current.Select(r => new FinalRow(r.Name, r.Value, r.SettingType, r.OrderNr)).ToList();
        foreach (var item in result.Items.Where(i => i.PhysicalTable == process + Table))
        {
            var type = item.SettingType ?? item.OldSettingType;
            var value = item.Value ?? item.OldValue;
            var key = FinalRow.KeyOf(item.Name!, value, type);
            rows.RemoveAll(r => r.Key == key && item.Action is "Update" or "Delete");
            if (item.Action is "Update" or "Insert")
            {
                rows.Add(new FinalRow(item.Name!, value, type!, item.OrderNr!.Value));
            }
        }

        return rows;
    }

    private sealed record FinalRow(string Name, string? Value, string SettingType, int OrderNr)
    {
        public string Key => KeyOf(Name, Value, SettingType);

        public static string KeyOf(string name, string? value, string? settingType) =>
            settingType == "SettingGroup" && !name.StartsWith("Detail.", StringComparison.Ordinal) ? name + "=" + value : name;
    }

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "groups", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string value, string? after = null, int? orderNr = null) =>
        new() { Table = Table, Name = name, Value = value, After = after, OrderNr = orderNr };

    private static SmDesiredRow Group(string value, string? after = null, int? orderNr = null) =>
        new() { Table = Table, Name = "Column", Value = value, SettingType = "SettingGroup", After = after, OrderNr = orderNr };
}
