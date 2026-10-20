using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SmOrchestratorTests(SqlServerFixture fixture)
{
    private const string Source = SqlServerFixture.PlanClientName;
    private const string Target = SqlServerFixture.TargetClientName;
    private const string Table = "DocumentListLayout";

    private SmOrchestrator Orchestrator => new(fixture.Sm);

    [Fact]
    public async Task Compare_IdenticalClients_NoChangesStatus()
    {
        var p = await NewProcessInBothAsync();

        var result = await Orchestrator.CompareClientsAsync(Source, Target, p);

        Assert.Equal("NoChanges", result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Compare_ShowsDifferences_WithoutStoring()
    {
        var p = await NewProcessInBothAsync();
        await PlanSandbox.ExecAsync(fixture, Source, $"UPDATE dbo.{p}{Table} SET VALUE = N'99' WHERE NAME = N'Grid.PageSize'");

        var result = await Orchestrator.CompareClientsAsync(Source, Target, p);

        Assert.Equal("DryRun", result.Status);
        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("99", item.Value);
        Assert.Equal("20", item.OldValue);
        Assert.Empty(await fixture.Sm.GetPlansAsync(Target, p));
    }

    [Fact]
    public async Task Clone_CreatesPlanOnTarget_ApplyMakesEqual()
    {
        var p = await NewProcessInBothAsync();
        await PlanSandbox.ExecAsync(fixture, Source, $"""
            UPDATE dbo.{p}{Table} SET VALUE = N'99' WHERE NAME = N'Grid.PageSize';
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.Extra', N'x', N'Setting', NULL, N'ALL', 70);
            {NewTableSql(p)}
            """);
        var session = await fixture.Sm.StartSessionAsync(Target, "clone", p);

        var plan = await Orchestrator.ClonePlanAsync(Source, Target, p, session.Id, options: new SmPlanOptions { CreateTable = true });

        Assert.Equal("Created", plan.Status);
        var applied = await fixture.Sm.ApplyPlanAsync(Target, plan.PlanId!.Value, plan.Checksum!);
        Assert.Equal("Applied", applied.Status);
        var scope = (await fixture.Sm.GetTablesAsync(Source, p)).Where(t => t.Owner == p).ToList();
        Assert.Contains(scope, t => t.View == "DocumentNewLayout");
        foreach (var t in scope)
        {
            var s = await fixture.Sm.GetSettingsAsync(Source, p, t.View, t.Layer);
            var d = await fixture.Sm.GetSettingsAsync(Target, p, t.View, t.Layer);
            Assert.Equal(Project(s), Project(d));
        }
    }

    [Fact]
    public async Task Clone_SkipsServerSettingRows()
    {
        var p = await NewProcessInBothAsync();
        await PlanSandbox.ExecAsync(fixture, Source, $"UPDATE dbo.{p}{Table} SET VALUE = N'zzz' WHERE NAME = N'Grid.Secret'; UPDATE dbo.{p}{Table} SET VALUE = N'99' WHERE NAME = N'Grid.PageSize'");
        var session = await fixture.Sm.StartSessionAsync(Target, "clone", p);

        var plan = await Orchestrator.ClonePlanAsync(Source, Target, p, session.Id);
        await fixture.Sm.ApplyPlanAsync(Target, plan.PlanId!.Value, plan.Checksum!);

        Assert.DoesNotContain(plan.Items, i => i.Name == "Grid.Secret");
        var rows = await PlanSandbox.RowsAsync(fixture, Target, p + Table);
        Assert.Equal("abc", rows.Single(r => r.Name == "Grid.Secret").Value);
    }

    [Fact]
    public async Task Clone_MissingTable_WithoutOption_ThrowsTableMissing()
    {
        var p = await NewProcessInBothAsync();
        await PlanSandbox.ExecAsync(fixture, Source, NewTableSql(p));
        var session = await fixture.Sm.StartSessionAsync(Target, "clone", p);

        var ex = await Assert.ThrowsAsync<SmException>(() => Orchestrator.ClonePlanAsync(Source, Target, p, session.Id));

        Assert.Equal(SmErrorCodes.TableMissing, ex.Code);
    }

    private static string NewTableSql(string p) => $"""
        CREATE TABLE dbo.{p}DocumentNewLayout (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
        INSERT INTO dbo.{p}DocumentNewLayout (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Foo', N'bar', N'Setting', NULL, N'ALL', 10);
        """;

    private static List<string> Project(IReadOnlyList<SmSettingRow> rows) =>
        rows.Where(r => r.SettingType != "ServerSetting")
            .OrderBy(r => r.OrderNr).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => $"{r.Name}|{r.Value}|{r.SettingType}|{r.ListValue}|{r.OrderNr}").ToList();

    private async Task<string> NewProcessInBothAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Source);
        await PlanSandbox.CreateProcessAsync(fixture, Target, process: p);
        return p;
    }
}
