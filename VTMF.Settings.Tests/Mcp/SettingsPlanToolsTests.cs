using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Tests.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsPlanToolsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    private SettingsPlanTools Tools => new(fixture.Sm, new SmOrchestrator(fixture.Sm));

    [Fact]
    public async Task CreatePlan_ThenApply_ViaTools_Applied()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await Tools.StartSession(Client, "tools", p, CancellationToken.None)).Data!;

        var created = await Tools.CreatePlan(Client, session.Id, PageSize(p, "35"), "page size", null, CancellationToken.None);

        Assert.True(created.Ok);
        Assert.Equal("Created", created.Data!.Status);
        var applied = await Tools.ApplyPlan(Client, created.Data.PlanId!.Value, created.Data.Checksum!, CancellationToken.None);
        Assert.True(applied.Ok);
        Assert.Equal("Applied", applied.Data!.Status);
        Assert.Equal("35", await PageSizeValueAsync(Client, p));
    }

    [Fact]
    public async Task ApplyPlan_WrongChecksum_ReturnsCodedError()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await Tools.StartSession(Client, "tools", p, CancellationToken.None)).Data!;
        var created = (await Tools.CreatePlan(Client, session.Id, PageSize(p, "36"), "page size", null, CancellationToken.None)).Data!;

        var result = await Tools.ApplyPlan(Client, created.PlanId!.Value, "wrong", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("checksum_mismatch", result.Error!.Code);
    }

    [Fact]
    public async Task CreatePlan_ClosedSession_ReturnsSessionClosed()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await Tools.StartSession(Client, "tools", p, CancellationToken.None)).Data!;
        var closed = await Tools.CloseSession(Client, session.Id, CancellationToken.None);
        Assert.True(closed.Ok);

        var result = await Tools.CreatePlan(Client, session.Id, PageSize(p, "37"), "page size", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("session_closed", result.Error!.Code);
    }

    [Fact]
    public async Task RollbackSession_ViaTools_RestoresValue()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await Tools.StartSession(Client, "tools", p, CancellationToken.None)).Data!;
        var created = (await Tools.CreatePlan(Client, session.Id, PageSize(p, "38"), "page size", null, CancellationToken.None)).Data!;
        await Tools.ApplyPlan(Client, created.PlanId!.Value, created.Checksum!, CancellationToken.None);
        Assert.Equal("38", await PageSizeValueAsync(Client, p));

        var rollback = await Tools.RollbackSession(Client, session.Id, CancellationToken.None);

        Assert.True(rollback.Ok);
        var applied = await Tools.ApplyPlan(Client, rollback.Data!.PlanId!.Value, rollback.Data.Checksum!, CancellationToken.None);
        Assert.Equal("Applied", applied.Data!.Status);
        Assert.Equal("20", await PageSizeValueAsync(Client, p));
    }

    [Fact]
    public async Task CompareClients_ViaTools_ReturnsDryRun()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        await PlanSandbox.CreateProcessAsync(fixture, SqlServerFixture.TargetClientName, p);
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET VALUE = N'99' WHERE NAME = N'Grid.PageSize'");

        var result = await Tools.CompareClients(Client, SqlServerFixture.TargetClientName, p, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("DryRun", result.Data!.Status);
        Assert.Contains(result.Data.Items, i => i.Name == "Grid.PageSize" && i.Value == "99");
    }

    [Fact]
    public async Task ListSessions_ViaTools_ReturnsSession()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await Tools.StartSession(Client, "tools list", p, CancellationToken.None)).Data!;

        var result = await Tools.ListSessions(Client, p, 20, CancellationToken.None);

        Assert.True(result.Ok);
        var listed = Assert.Single(result.Data!);
        Assert.Equal(session.Id, listed.Id);
        Assert.Equal("tools list", listed.Name);
        Assert.Equal("Open", listed.Status);
    }

    [Fact]
    public async Task SessionChanges_ViaTools_ReturnsNetChange()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var (session, _) = await StartAndApplyAsync(p, "41");

        var result = await Tools.SessionChanges(Client, session.Id, CancellationToken.None);

        Assert.True(result.Ok);
        var item = Assert.Single(result.Data!);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("41", item.Value);
        Assert.Equal("20", item.OldValue);
    }

    [Fact]
    public async Task GetPlan_ViaTools_ReturnsPlanWithItems()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var (session, planId) = await StartAndApplyAsync(p, "42");

        var result = await Tools.GetPlan(Client, planId, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(planId, result.Data!.Plan.Id);
        Assert.Equal(session.Id, result.Data.Plan.SessionId);
        Assert.Equal("Applied", result.Data.Plan.Status);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("42", item.Value);
    }

    [Fact]
    public async Task ListPlans_ViaTools_ReturnsSessionPlans()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var (session, planId) = await StartAndApplyAsync(p, "43");

        var result = await Tools.ListPlans(Client, null, session.Id, 20, CancellationToken.None);

        Assert.True(result.Ok);
        var plan = Assert.Single(result.Data!);
        Assert.Equal(planId, plan.Id);
        Assert.Equal(p, plan.Process);
        Assert.Equal("Applied", plan.Status);
    }

    [Fact]
    public async Task RollbackPlan_ViaTools_RestoresValue()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var (_, planId) = await StartAndApplyAsync(p, "44");

        var rollback = await Tools.RollbackPlan(Client, planId, CancellationToken.None);

        Assert.True(rollback.Ok);
        Assert.Equal("Created", rollback.Data!.Status);
        var item = Assert.Single(rollback.Data.Items);
        Assert.Equal("20", item.Value);
        Assert.Equal("44", item.OldValue);
        var applied = await Tools.ApplyPlan(Client, rollback.Data.PlanId!.Value, rollback.Data.Checksum!, CancellationToken.None);
        Assert.Equal("Applied", applied.Data!.Status);
        Assert.Equal("20", await PageSizeValueAsync(Client, p));
    }

    [Fact]
    public async Task ClonePlan_ViaTools_CreatesPlanOnTarget()
    {
        const string target = SqlServerFixture.TargetClientName;
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        await PlanSandbox.CreateProcessAsync(fixture, target, p);
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET VALUE = N'45' WHERE NAME = N'Grid.PageSize'");
        var session = (await Tools.StartSession(target, "tools clone", p, CancellationToken.None)).Data!;

        var result = await Tools.ClonePlan(Client, target, p, session.Id, null, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Created", result.Data!.Status);
        Assert.Contains(result.Data.Items, i => i.Name == "Grid.PageSize" && i.Value == "45");
        var stored = await Tools.GetPlan(target, result.Data.PlanId!.Value, CancellationToken.None);
        Assert.Equal(session.Id, stored.Data!.Plan.SessionId);
    }

    [Fact]
    public async Task LegacyEnvMigrationPlan_ViaTools_CreatesPlan()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.PageSize', N'46', N'Setting', NULL, N'wterminal2', 55);");
        var session = (await Tools.StartSession(Client, "tools migrate", p, CancellationToken.None)).Data!;

        var result = await Tools.LegacyEnvMigrationPlan(Client, p, session.Id, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Created", result.Data!.Status);
        Assert.Equal(["CreateTable", "Insert", "Delete"], result.Data.Items.Select(i => i.Action).ToArray());
        Assert.Contains(result.Data.Items, i => i.Action == "Insert" && i.PhysicalTable == p + Table + "wterminal2" && i.Value == "46");
    }

    [Fact]
    public async Task Tools_OutdatedClient_ReturnModuleOutdated()
    {
        var result = await Tools.StartSession(SqlServerFixture.OutdatedClientName, "tools", "anything", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.ModuleOutdated, result.Error!.Code);
    }

    /// <summary>Starts a session and applies a Grid.PageSize change in it. Returns (session, applied plan id).</summary>
    private async Task<(SmSession Session, int PlanId)> StartAndApplyAsync(string process, string value)
    {
        var session = (await Tools.StartSession(Client, "tools", process, CancellationToken.None)).Data!;
        var created = (await Tools.CreatePlan(Client, session.Id, PageSize(process, value), "page size", null, CancellationToken.None)).Data!;
        var applied = await Tools.ApplyPlan(Client, created.PlanId!.Value, created.Checksum!, CancellationToken.None);
        Assert.Equal("Applied", applied.Data!.Status);
        return (session, created.PlanId.Value);
    }

    private static SmDesiredState PageSize(string process, string value) => new()
    {
        Process = process,
        Mode = "merge",
        Rows = [new SmDesiredRow { Table = Table, Layer = "base", Name = "Grid.PageSize", Value = value }],
    };

    private async Task<string?> PageSizeValueAsync(string client, string process) =>
        (await PlanSandbox.RowsAsync(fixture, client, process + Table)).Single(r => r.Name == "Grid.PageSize").Value;
}
