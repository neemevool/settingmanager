using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

/// <summary>
/// The plan engine on a database whose collation (Estonian_CI_AS_WS) differs from tempdb's: temp tables must use the
/// database collation, or joins with settings tables fail (Msg 468) and identity matching differs from ApplyPlan's.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class CollationTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.CollateClientName;
    private const string Table = "DocumentListLayout";
    private const string Env = "wterminal2";

    [Fact]
    public async Task CreateAndApply_Succeeds()
    {
        var (p, session) = await NewProcessAsync();

        await ApplyAsync(p, session, Row("Grid.PageSize", "50"), Row("Grid.ShowFilterRow", "true"));

        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("50", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("true", Assert.Single(rows, r => r.Name == "Grid.ShowFilterRow").Value);
    }

    [Fact]
    public async Task CreateAndApply_NameDifferentOnlyInDatabaseCollation_IsANewRow()
    {
        // Equal in tempdb's width-insensitive collation, different in the width-sensitive database collation.
        const string wideName = "Grid.PageSizｅ";
        var (p, session) = await NewProcessAsync();

        var plan = await fixture.Sm.CreatePlanAsync(Client, Desired(p, Row(wideName, "99")), "width", session.Id);

        var item = Assert.Single(plan.Items);
        Assert.Equal("Insert", item.Action);
        Assert.Equal(wideName, item.Name);
        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);
        Assert.Equal("Applied", result.Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("99", Assert.Single(rows, r => r.Name == wideName).Value);
    }

    [Fact]
    public async Task RollbackPlan_CreateAndApply_Succeeds()
    {
        var (p, session) = await NewProcessAsync();
        var original = await ApplyAsync(p, session, Row("Grid.PageSize", "50"));

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        Assert.Equal("Created", rollback.Status);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
    }

    [Fact]
    public async Task SessionRollback_CreateAndApply_Succeeds()
    {
        var (p, session) = await NewProcessAsync();
        await ApplyAsync(p, session, Row("Grid.PageSize", "50"));
        await ApplyAsync(p, session, Row("Grid.ShowFilterRow", "true"));
        Assert.Equal(2, (await fixture.Sm.GetSessionChangesAsync(Client, session.Id)).Count);

        var rollback = await fixture.Sm.CreateSessionRollbackPlanAsync(Client, session.Id);

        Assert.Equal("Created", rollback.Status);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.DoesNotContain(rows, r => r.Name == "Grid.ShowFilterRow");
    }

    [Fact]
    public async Task LegacyEnvMigrationPlan_CreateAndApply_Succeeds()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.PageSize', N'30', N'Setting', NULL, N'{Env}', 55);");

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("Created", plan.Status);
        Assert.Equal(["CreateTable", "Insert", "Delete"], plan.Items.Select(i => i.Action).ToArray());
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!)).Status);
        var overlay = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table + Env, Env));
        Assert.Equal("30", overlay.Value);
        Assert.Empty(await PlanSandbox.RowsAsync(fixture, Client, p + Table, Env));
    }

    private async Task<int> ApplyAsync(string process, SmSession session, params SmDesiredRow[] rows)
    {
        var plan = await fixture.Sm.CreatePlanAsync(Client, Desired(process, rows), "apply", session.Id);
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

    private static SmDesiredRow Row(string name, string value) => new() { Table = Table, Name = name, Value = value };
}
