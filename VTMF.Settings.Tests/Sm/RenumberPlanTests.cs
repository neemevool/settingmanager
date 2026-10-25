using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

// Renumber plan (spec 2026-10-24 setting groups, section 6).
// Sandbox table {p}DocumentListLayout: Column=DOCNR 10, Column.DOCNR.Caption 20, Column=DATE 30,
// Column.DATE.Caption 40, Grid.PageSize 50, Grid.Secret (ServerSetting) 60.
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class RenumberPlanTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task TidyTable_NoChanges()
    {
        var (p, session) = await NewProcessAsync();

        var result = await RenumberAsync(p, session);

        Assert.Equal("NoChanges", result.Status);
        Assert.Empty(result.Items);
        Assert.Null(result.PlanId);
    }

    [Fact]
    public async Task SplitBlock_Joined()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderAsync(p, "Column.DOCNR.Caption", 45);

        var result = await RenumberAsync(p, session);

        Assert.Equal("DryRun", result.Status);
        var item = Assert.Single(result.Items);
        AssertOrderUpdate(item, "Column.DOCNR.Caption", "Dok nr", 45, 20);
    }

    [Fact]
    public async Task ChildBeforeGroup_MovedToBlock()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderAsync(p, "Column.DATE.Caption", 5);

        var result = await RenumberAsync(p, session);

        var item = Assert.Single(result.Items);
        AssertOrderUpdate(item, "Column.DATE.Caption", "Kuupäev", 5, 40);
    }

    [Fact]
    public async Task Orphans_GoLast_AndWarn()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Column.OLD.Caption', N'Old', N'Setting', NULL, N'ALL', 15);");

        var result = await RenumberAsync(p, session);

        // Without the orphan the table is already 10..60, so only the orphan moves (to the end).
        var orphan = Assert.Single(result.Items);
        AssertOrderUpdate(orphan, "Column.OLD.Caption", "Old", 15, 70);
        var warning = Assert.Single(result.Warnings, w => w.Code == "ORPHAN_ROWS");
        Assert.Contains("Column.OLD.Caption", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TiedSiblingGroups_OrderedByValue()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"UPDATE dbo.{p}{Table} SET ordernr = 10 WHERE NAME = N'Column' AND VALUE = N'DATE';");

        var result = await RenumberAsync(p, session);

        var final = await OrderAsync(p);
        foreach (var item in result.Items)
            final[Key(item.Name!, item.SettingType, item.Value ?? item.OldValue)] = item.OrderNr!.Value;

        Assert.Equal(10, final["Column=DATE"]);
        Assert.Equal(20, final["Column.DATE.Caption"]);
        Assert.Equal(30, final["Column=DOCNR"]);
        Assert.Equal(40, final["Column.DOCNR.Caption"]);
        Assert.Equal(50, final["Grid.PageSize"]);
        Assert.Equal(60, final["Grid.Secret"]);
    }

    [Fact]
    public async Task ServerSettingRow_ValuesNeverReturned()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderAsync(p, "Grid.Secret", 1);

        var result = await RenumberAsync(p, session);

        var secret = Assert.Single(result.Items, i => i.Name == "Grid.Secret");
        Assert.Equal("Update", secret.Action);
        Assert.Null(secret.Value);
        Assert.Null(secret.OldValue);
        Assert.Equal(1, secret.OldOrderNr);
        Assert.Equal(10, secret.OrderNr);
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("abc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicates_Refused()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.Secret', N'xyz', N'ServerSetting', NULL, N'ALL', 70);");

        var ex = await Assert.ThrowsAsync<SmException>(() => RenumberAsync(p, session));

        Assert.Equal(SmErrorCodes.DuplicateRow, ex.Code);
        Assert.Contains("Grid.Secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("xyz", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerProcessSettings_Refused()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreateRenumberPlanAsync(Client, p, "ServerProcessSettings", "base", "renumber", session.Id, dryRun: true));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
        Assert.Contains("ServerProcessSettings", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_ThenRollback_RestoresOrder()
    {
        var (p, session) = await NewProcessAsync();
        await SetOrderAsync(p, "Column.DATE.Caption", 5);
        await SetOrderAsync(p, "Grid.Secret", 1);
        var before = await OrderAsync(p);

        var plan = await fixture.Sm.CreateRenumberPlanAsync(Client, p, Table, "base", "tidy", session.Id);
        Assert.Equal("Created", plan.Status);
        var stored = (await fixture.Sm.GetPlanAsync(Client, plan.PlanId!.Value)).Plan;
        Assert.Equal("renumber", stored.Source);
        Assert.Equal("tidy", stored.Description);
        Assert.Equal(session.Id, stored.SessionId);

        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId.Value, plan.Checksum!)).Status);
        var tidy = await OrderAsync(p);
        Assert.Equal(10, tidy["Grid.Secret"]);
        Assert.Equal(20, tidy["Column=DOCNR"]);
        Assert.Equal(30, tidy["Column.DOCNR.Caption"]);
        Assert.Equal(40, tidy["Column=DATE"]);
        Assert.Equal(50, tidy["Column.DATE.Caption"]);
        Assert.Equal(60, tidy["Grid.PageSize"]);
        var secret = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table), r => r.Name == "Grid.Secret");
        Assert.Equal("abc", secret.Value);

        Assert.Equal("NoChanges", (await RenumberAsync(p, session)).Status);

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);

        Assert.Equal(before, await OrderAsync(p));
    }

    private static void AssertOrderUpdate(SmPlanItem item, string name, string value, int oldOrderNr, int orderNr)
    {
        Assert.Equal("Update", item.Action);
        Assert.Equal(name, item.Name);
        Assert.Equal(value, item.Value);
        Assert.Equal(value, item.OldValue);
        Assert.Equal(oldOrderNr, item.OldOrderNr);
        Assert.Equal(orderNr, item.OrderNr);
    }

    private static string Key(string name, string? settingType, string? value) =>
        settingType == "SettingGroup" ? name + "=" + value : name;

    private Task SetOrderAsync(string process, string name, int orderNr) =>
        PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{process}{Table} SET ordernr = {orderNr} WHERE NAME = N'{name}';");

    private async Task<Dictionary<string, int>> OrderAsync(string process) =>
        (await PlanSandbox.RowsAsync(fixture, Client, process + Table)).ToDictionary(
            r => Key(r.Name, r.SettingType, r.Value), r => r.OrderNr);

    private Task<SmPlanResult> RenumberAsync(string process, SmSession session) =>
        fixture.Sm.CreateRenumberPlanAsync(Client, process, Table, "base", "renumber", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }
}
