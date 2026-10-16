using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ModuleInfoTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetModuleStatus_InstalledClient_ReturnsVersion()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.ClientName);

        Assert.True(status.Installed);
        Assert.Equal(1, status.Version);
    }

    [Fact]
    public async Task GetModuleStatus_ClientWithoutModule_ReportsNotInstalled()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.EmptyClientName);

        Assert.False(status.Installed);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetModuleInfo_ClientWithoutModule_ThrowsModuleNotInstalled()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetModuleInfoAsync(SqlServerFixture.EmptyClientName));

        Assert.Equal(SmErrorCodes.ModuleNotInstalled, ex.Code);
    }
}
