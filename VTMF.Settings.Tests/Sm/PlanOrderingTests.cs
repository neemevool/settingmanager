using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanOrderingTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task Insert_AfterAnchor_UsesMidpoint()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column.DOCNR.Caption"));

        var item = Assert.Single(result.Items);
        Assert.Equal("Insert", item.Action);
        Assert.Equal(25, item.OrderNr);
        Assert.DoesNotContain(result.Warnings, w => w.Code is "ANCHOR_NOT_FOUND" or "RENUMBER");
    }

    [Fact]
    public async Task Insert_AfterGroupAnchor()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DATE.Width", "100", after: "Column=DATE"));

        Assert.Equal(35, Assert.Single(result.Items).OrderNr);
    }

    [Fact]
    public async Task Insert_NoAfter_UsesLongestPrefix()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DATE.Width", "100"));

        Assert.Equal(45, Assert.Single(result.Items).OrderNr);
    }

    [Fact]
    public async Task Insert_NoMatch_AppendsAtEnd()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Zzz.New", "1"));

        Assert.Equal(70, Assert.Single(result.Items).OrderNr);
    }

    [Fact]
    public async Task Insert_NoGap_RenumbersTable()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 11 WHERE NAME = N'Column.DOCNR.Caption';");

        var result = await PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column=DOCNR"));

        var insert = Assert.Single(result.Items, i => i.Action == "Insert");
        Assert.Equal("Column.DOCNR.Width", insert.Name);
        Assert.Equal(20, insert.OrderNr);
        var updates = result.Items.Where(i => i.Action == "Update")
            .Select(i => (i.Name, i.OldValue, i.OldOrderNr, i.OrderNr))
            .OrderBy(u => u.OldOrderNr)
            .ToArray();
        Assert.Equal(
            [
                ("Column.DOCNR.Caption", (string?)"Dok nr", (int?)11, (int?)30),
                ("Column", "DATE", 30, 40),
                ("Column.DATE.Caption", "Kuupäev", 40, 50),
                ("Grid.PageSize", "20", 50, 60),
                ("Grid.Secret", null, 60, 70),
            ],
            updates);
        var secret = Assert.Single(result.Items, i => i.Name == "Grid.Secret");
        Assert.Null(secret.Value);
        Assert.Null(secret.OldValue);
        Assert.Null(secret.ListValue);
        Assert.Null(secret.OldListValue);
        Assert.Equal("ServerSetting", secret.OldSettingType);
        Assert.DoesNotContain(result.Items, i => i.Name == "Column" && (i.Value ?? i.OldValue) == "DOCNR");
        Assert.All(result.Items.Where(i => i.Action == "Update" && i.Name != "Grid.Secret"), i =>
        {
            Assert.Equal(i.OldValue, i.Value);
            Assert.Equal(i.OldSettingType, i.SettingType);
        });
        var renumber = Assert.Single(result.Warnings, w => w.Code == "RENUMBER");
        Assert.Contains(p + Table, renumber.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", string.Join("|", result.Warnings.Select(w => w.Message)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renumber_WithDuplicateUntouchedRow_ThrowsDuplicateRow()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            UPDATE dbo.{p}{Table} SET ordernr = 11 WHERE NAME = N'Column.DOCNR.Caption';
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr)
            VALUES (N'Grid.Secret', N'abc', N'ServerSetting', NULL, N'ALL', 60);
            """);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column=DOCNR")));

        Assert.Equal(SmErrorCodes.DuplicateRow, ex.Code);
        Assert.Contains(p + Table, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Grid.Secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoRenumber_WithDuplicateUntouchedRow_PlansNormally()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.Secret', N'abc', N'ServerSetting', NULL, N'ALL', 60);");

        var result = await PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column.DOCNR.Caption"));

        Assert.Equal(25, Assert.Single(result.Items).OrderNr);
    }

    [Fact]
    public async Task Insert_AnchorNotFound_WarnsAndFallsBack()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, Row("Column.DATE.Width", "100", after: "No.Such.Row"));

        Assert.Equal(45, Assert.Single(result.Items).OrderNr);
        var warning = Assert.Single(result.Warnings, w => w.Code == "ANCHOR_NOT_FOUND");
        Assert.Contains("No.Such.Row", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoNewRows_SameAnchor_KeepDesiredOrder()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Row("Grid.X", "1", after: "Grid.PageSize"),
            Row("Grid.Y", "2", after: "Grid.PageSize"));

        var x = Assert.Single(result.Items, i => i.Name == "Grid.X").OrderNr;
        var y = Assert.Single(result.Items, i => i.Name == "Grid.Y").OrderNr;
        Assert.True(x > 50 && x < y && y < 60, $"X={x}, Y={y}");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "RENUMBER");
    }

    [Fact]
    public async Task Append_ConsidersSamePlanExplicitOrderNr()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Row("Zzz.Fixed", "1", orderNr: 100),
            Row("Yyy.Appended", "2"));

        Assert.Equal(100, Assert.Single(result.Items, i => i.Name == "Zzz.Fixed").OrderNr);
        Assert.Equal(110, Assert.Single(result.Items, i => i.Name == "Yyy.Appended").OrderNr);
    }

    [Fact]
    public async Task ServerProcessSettings_NewRowGetsZero()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session, new SmDesiredRow { Table = "ServerProcessSettings", Name = "Function.New", Value = "1" });

        Assert.Equal(0, Assert.Single(result.Items).OrderNr);
    }

    [Fact]
    public async Task Insert_AfterEarlierNewRow()
    {
        var (p, session) = await NewProcessAsync();

        var result = await PlanAsync(p, session,
            Row("Zzz.A", "1"),
            Row("Zzz.B", "2", after: "Zzz.A"));

        Assert.Equal(70, Assert.Single(result.Items, i => i.Name == "Zzz.A").OrderNr);
        Assert.Equal(80, Assert.Single(result.Items, i => i.Name == "Zzz.B").OrderNr);
    }

    [Fact]
    public async Task ServerSetting_StillRefused_WithOrderNrChange()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            PlanAsync(p, session, new SmDesiredRow { Table = Table, Name = "Grid.Secret", Value = "abc", SettingType = "ServerSetting", OrderNr = 65 }));

        Assert.Equal(SmErrorCodes.ServerSetting, ex.Code);
    }

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "ordering", session.Id, dryRun: true);

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string value, string? after = null, int? orderNr = null) =>
        new() { Table = Table, Name = name, Value = value, After = after, OrderNr = orderNr };
}
