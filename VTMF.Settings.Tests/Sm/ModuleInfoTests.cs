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
        Assert.Equal(fixture.Scripts.Version, status.Version);
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

    [Fact]
    public async Task GetModuleStatus_Outdated_ReportsState()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.OutdatedClientName);

        Assert.Equal("outdated", status.State);
        Assert.Equal(1, status.Version);
        Assert.Equal(fixture.Scripts.Version, status.AvailableVersion);
    }

    [Fact]
    public async Task GetModuleStatus_Newer_ReportsState()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.NewerClientName);

        Assert.Equal("newer_than_tool", status.State);
        Assert.Equal(fixture.Scripts.Version + 1, status.Version);
    }

    [Fact]
    public async Task GetModuleStatus_UpToDate_ReportsState()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.ClientName);

        Assert.Equal("up_to_date", status.State);
    }

    [Fact]
    public async Task GetModuleStatus_NotInstalled_ReportsStateAndAvailableVersion()
    {
        var status = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.EmptyClientName);

        Assert.Equal("not_installed", status.State);
        Assert.Equal(fixture.Scripts.Version, status.AvailableVersion);
    }
}
