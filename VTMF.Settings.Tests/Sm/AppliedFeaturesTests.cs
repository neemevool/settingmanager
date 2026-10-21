using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class AppliedFeaturesTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task Applied_FeaturePlan_IsListed()
    {
        var (p, planId) = await ApplyAsync("feature:42:Test feature", "50");

        var listed = Assert.Single(await fixture.Sm.GetAppliedFeaturesAsync(Client, p));

        Assert.Equal(42, listed.FeatureId);
        Assert.Equal("Test feature", listed.FeatureName);
        Assert.Equal(p, listed.Process);
        Assert.Equal(planId, listed.PlanId);
        Assert.NotEmpty(listed.AppliedBy);
    }

    [Fact]
    public async Task RolledBack_IsExcluded()
    {
        var (p, planId) = await ApplyAsync("feature:43:Gone", "50");
        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, planId);
        await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);

        Assert.Empty(await fixture.Sm.GetAppliedFeaturesAsync(Client, p));
    }

    [Fact]
    public async Task Manual_IsNotListed()
    {
        var (p, _) = await ApplyAsync("manual", "50");

        Assert.Empty(await fixture.Sm.GetAppliedFeaturesAsync(Client, p));
    }

    private async Task<(string Process, int PlanId)> ApplyAsync(string source, string value)
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        var desired = new SmDesiredState
        {
            Process = p,
            Rows = [new SmDesiredRow { Table = Table, Name = "Grid.PageSize", Value = value }],
        };
        var plan = await fixture.Sm.CreatePlanAsync(Client, desired, "apply", session.Id, source: source);
        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);
        Assert.Equal("Applied", result.Status);
        return (p, plan.PlanId.Value);
    }
}
