using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SmModuleStatusTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetModuleStatus_IgnoresOtherModuleRows()
    {
        // 'aaamodule' sorts before 'settingsmanagement' on the clustered PK, so a first-row read would pick it.
        await fixture.ExecuteOnTestAsync("INSERT sm.moduleinfo (module, version) VALUES (N'aaamodule', 999)");
        try
        {
            var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.ClientName);

            Assert.Equal("up_to_date", status.State);
        }
        finally
        {
            await fixture.ExecuteOnTestAsync("DELETE sm.moduleinfo WHERE module = N'aaamodule'");
        }
    }
}
