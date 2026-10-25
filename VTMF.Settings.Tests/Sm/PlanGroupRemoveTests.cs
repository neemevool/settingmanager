using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

// op remove of SettingGroup rows (spec 2026-10-24 setting groups, sections 2 and 5).
// Sandbox table {p}DocumentListLayout: Column=DOCNR 10, Column.DOCNR.Caption 20, Column=DATE 30,
// Column.DATE.Caption 40, Grid.PageSize 50, Grid.Secret (ServerSetting) 60.
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanGroupRemoveTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task RemoveGroup_DeletesWholeBlock()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("Delete", i.Action));
        Assert.All(result.Items, i => Assert.Equal(p + Table, i.PhysicalTable));
        Assert.Single(result.Items, i => i.Name == "Column" && i.OldValue == "DOCNR");
        Assert.Single(result.Items, i => i.Name == "Column.DOCNR.Caption" && i.OldValue == "Dok nr");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoveGroup_AlsoDeletesLangChildren()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"));

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("Delete", i.Action));
        var en = Assert.Single(result.Items, i => i.PhysicalTable == p + Table + "en");
        Assert.Equal("Column.DOCNR.Caption", en.Name);
        Assert.Equal("Doc no", en.OldValue);
        Assert.Equal("lang:en", en.Layer);
        Assert.Equal(Table, en.Table);
        Assert.Equal("ALL", en.WorkerCode);
        Assert.Equal(10, en.OldOrderNr);
        Assert.DoesNotContain(result.Items, i => i.Name == "Column.DATE.Caption");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoveGroup_KeepsLangChildren_WhenGroupRemainsInEnv()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "wt2", "wt2",
            ("Column", "DOCNR", "SettingGroup", 10));

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(p + Table, i.PhysicalTable));
    }

    [Fact]
    public async Task RemoveGroup_FromBaseAndEnv_DeletesLangChildrenOnce()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "wt2", "wt2",
            ("Column", "DOCNR", "SettingGroup", 10));

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"), RemoveGroup("DOCNR", "env:wt2"));

        Assert.Equal(4, result.Items.Count);
        Assert.Single(result.Items, i => i.PhysicalTable == p + Table + "wt2" && i.Name == "Column" && i.WorkerCode == "wt2");
        var en = Assert.Single(result.Items, i => i.PhysicalTable == p + Table + "en");
        Assert.Equal("Column.DOCNR.Caption", en.Name);
    }

    [Fact]
    public async Task RemoveGroup_KeepsLangChildren_WhenGroupAddedToEnvInSamePlan()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "wt2", "wt2");

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"),
            new SmDesiredRow { Table = Table, Layer = "env:wt2", Name = "Column", Value = "DOCNR", SettingType = "SettingGroup" });

        Assert.DoesNotContain(result.Items, i => i.PhysicalTable == p + Table + "en");
        Assert.Single(result.Items, i => i.Action == "Insert" && i.PhysicalTable == p + Table + "wt2");
    }

    [Fact]
    public async Task RemoveGroup_NoLangTable_Works()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, RemoveGroup("DATE"));

        Assert.Equal(2, result.Items.Count);
        Assert.Single(result.Items, i => i.Name == "Column" && i.OldValue == "DATE");
        Assert.Single(result.Items, i => i.Name == "Column.DATE.Caption");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoveGroup_LangTableWithoutItsRows_Works()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "en", "ALL",
            ("Column.DATE.Caption", "Date", "Setting", 20));

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(p + Table, i.PhysicalTable));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoveGroup_FromLangTable_OnlyThatBlock()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "en", "ALL",
            ("Column", "DOCNR", "SettingGroup", 5),
            ("Column.DOCNR.Caption", "Doc no", "Setting", 10),
            ("Column.DATE.Caption", "Date", "Setting", 20));

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR", "lang:en"));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal(p + Table + "en", i.PhysicalTable));
        Assert.All(result.Items, i => Assert.Equal("lang:en", i.Layer));
        Assert.Single(result.Items, i => i.Name == "Column" && i.OldValue == "DOCNR");
        Assert.Single(result.Items, i => i.Name == "Column.DOCNR.Caption");
    }

    [Fact]
    public async Task RemoveGroup_PlusChildUpsert_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            RemoveGroup("DOCNR"), new SmDesiredRow { Table = Table, Name = "Column.DOCNR.Width", Value = "1" }));

        Assert.Equal(SmErrorCodes.GroupRemoveConflict, ex.Code);
        Assert.Contains("Column.DOCNR.Width", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Column=DOCNR", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveGroup_PlusMove_Refused()
    {
        var (p, session) = await NewProcessAsync();

        // Same identity as the remove: reported as group_remove_conflict, not as a duplicate row (invalid_desired).
        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            RemoveGroup("DOCNR"),
            new SmDesiredRow { Table = Table, Name = "Column", Value = "DOCNR", SettingType = "SettingGroup", Op = "move", After = "Column=DATE" }));

        Assert.Equal(SmErrorCodes.GroupRemoveConflict, ex.Code);
        Assert.Contains("Column=DOCNR", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveGroup_PlusUpsertOfSameGroup_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Column", Value = "DOCNR", SettingType = "SettingGroup" },
            RemoveGroup("DOCNR")));

        Assert.Equal(SmErrorCodes.GroupRemoveConflict, ex.Code);
    }

    [Fact]
    public async Task RemoveGroup_PlusMoveOfOtherGroup_Allowed()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, RemoveGroup("DOCNR"),
            new SmDesiredRow { Table = Table, Name = "Column", Value = "DATE", SettingType = "SettingGroup", Op = "move", After = "^" });

        Assert.Equal(2, result.Items.Count(i => i.Action == "Delete"));
        Assert.DoesNotContain(result.Warnings, w => w.Severity == "Error");
    }

    [Fact]
    public async Task RemoveGroup_BlockWithServerSetting_Refused()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Column.DOCNR.Key', N'secret', N'ServerSetting', NULL, N'ALL', 25);");

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session, RemoveGroup("DOCNR")));

        Assert.Equal(SmErrorCodes.ServerSetting, ex.Code);
        Assert.Contains("Column.DOCNR.Key", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveNameKeyedGroup_DeletesBlockAndLangChildren()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);
        await CreateDetailEnAsync(p);

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Detail.Lines", SettingType = "SettingGroup", Op = "remove" });

        AssertDetailLinesRemoved(p, result);
    }

    // F1: the desired row has no settingtype (defaults to Setting); a name-keyed group row matches by NAME alone and
    // the matched current row decides that this is a group remove.
    [Fact]
    public async Task RemoveNameKeyedGroup_WithoutSettingType_DeletesBlockAndLangChildren()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);
        await CreateDetailEnAsync(p);

        var result = await PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Detail.Lines", Op = "remove" });

        AssertDetailLinesRemoved(p, result);
    }

    [Fact]
    public async Task RemoveNameKeyedGroup_WithoutSettingType_PlusChildUpsert_Refused()
    {
        var (p, session) = await NewProcessAsync();
        await AddDetailGroupsAsync(p);

        var ex = await Assert.ThrowsAsync<SmException>(() => PlanAsync(p, session,
            new SmDesiredRow { Table = Table, Name = "Detail.Lines", Op = "remove" },
            new SmDesiredRow { Table = Table, Name = "Detail.Lines.Width", Value = "5" }));

        Assert.Equal(SmErrorCodes.GroupRemoveConflict, ex.Code);
        Assert.Contains("Detail.Lines.Width", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveChild_OnlyThatRow()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);

        var result = await PlanAsync(p, session, new SmDesiredRow { Table = Table, Name = "Column.DOCNR.Caption", Op = "remove" });

        var item = Assert.Single(result.Items);
        Assert.Equal("Delete", item.Action);
        Assert.Equal(p + Table, item.PhysicalTable);
        Assert.Equal("Column.DOCNR.Caption", item.Name);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoveGroup_ApplyThenRollback_RestoresBlockAndLang()
    {
        var (p, session) = await NewProcessAsync();
        await CreateEnAsync(p);
        var baseBefore = await SnapshotAsync(p + Table);
        var enBefore = await SnapshotAsync(p + Table + "en");

        var plan = await fixture.Sm.CreatePlanAsync(Client,
            new SmDesiredState { Process = p, Rows = [RemoveGroup("DOCNR")] }, "remove", session.Id);
        Assert.Equal("Created", plan.Status);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!)).Status);

        var baseAfter = await SnapshotAsync(p + Table);
        Assert.DoesNotContain("Column=DOCNR", baseAfter);
        Assert.DoesNotContain(baseAfter, r => r.StartsWith("Column.DOCNR.", StringComparison.Ordinal));
        Assert.Equal(["Column.DATE.Caption|Date|20"], await SnapshotAsync(p + Table + "en"));

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);

        Assert.Equal(baseBefore, await SnapshotAsync(p + Table));
        Assert.Equal(enBefore, await SnapshotAsync(p + Table + "en"));
    }

    private Task AddDetailGroupsAsync(string p) =>
        PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Detail.Lines',         N'x',     N'SettingGroup', NULL, N'ALL', 70),
             (N'Detail.Lines.Caption', N'Lines', N'Setting',      NULL, N'ALL', 80),
             (N'Detail.Notes',         N'y',     N'SettingGroup', NULL, N'ALL', 90),
             (N'Detail.Notes.Caption', N'Notes', N'Setting',      NULL, N'ALL', 100);
            """);

    private Task CreateDetailEnAsync(string p) =>
        PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "en", "ALL",
            ("Detail.Lines.Caption", "Lines en", "Setting", 10),
            ("Detail.Notes.Caption", "Notes en", "Setting", 20));

    private static void AssertDetailLinesRemoved(string p, SmPlanResult result)
    {
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("Delete", i.Action));
        Assert.Single(result.Items, i => i.PhysicalTable == p + Table && i.Name == "Detail.Lines");
        Assert.Single(result.Items, i => i.PhysicalTable == p + Table && i.Name == "Detail.Lines.Caption");
        var en = Assert.Single(result.Items, i => i.PhysicalTable == p + Table + "en");
        Assert.Equal("Detail.Lines.Caption", en.Name);
        Assert.DoesNotContain(result.Items, i => (i.Name ?? "").StartsWith("Detail.Notes", StringComparison.Ordinal));
        Assert.Empty(result.Warnings);
    }

    private Task CreateEnAsync(string p) =>
        PlanSandbox.CreateLayerTableAsync(fixture, Client, p + Table + "en", "ALL",
            ("Column.DOCNR.Caption", "Doc no", "Setting", 10),
            ("Column.DATE.Caption", "Date", "Setting", 20));

    private async Task<List<string>> SnapshotAsync(string physicalTable) =>
        (await PlanSandbox.RowsAsync(fixture, Client, physicalTable))
            .Select(r => r.SettingType == "SettingGroup" ? $"{r.Name}={r.Value}" : $"{r.Name}|{r.Value}|{r.OrderNr}")
            .ToList();

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "groups", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow RemoveGroup(string value, string layer = "base") =>
        new() { Table = Table, Layer = layer, Name = "Column", Value = value, SettingType = "SettingGroup", Op = "remove" };
}
