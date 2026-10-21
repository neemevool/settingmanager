using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanTablesTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData("sm.session")]
    [InlineData("sm.plan")]
    [InlineData("sm.planitem")]
    [InlineData("sm.planwarning")]
    public async Task Install_CreatesPlanTables(string table)
    {
        var id = await fixture.ScalarOnAsync<int?>(SqlServerFixture.PlanClientName, $"SELECT OBJECT_ID('{table}', 'U')");

        Assert.NotNull(id);
    }

    [Fact]
    public async Task Install_IsIdempotent()
    {
        await fixture.Installer.InstallAsync(SqlServerFixture.PlanClientName);
        await fixture.Installer.InstallAsync(SqlServerFixture.PlanClientName);

        var version = await fixture.ScalarOnAsync<int>(SqlServerFixture.PlanClientName, "SELECT version FROM sm.moduleinfo");
        Assert.Equal(6, version);
    }
}
