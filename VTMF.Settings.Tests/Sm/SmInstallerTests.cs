using VTMF.Settings.Core.Sm;
using VTMF.Settings.Services;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SmInstallerTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Install_FreshDatabase_InstallsCurrentVersion()
    {
        var result = await fixture.Installer.InstallAsync(SqlServerFixture.FreshClientName);

        Assert.Equal("not_installed", result.PreviousState);
        Assert.Null(result.PreviousVersion);
        Assert.Equal(fixture.Scripts.Version, result.InstalledVersion);
        Assert.Equal(fixture.Scripts.Scripts.Count, result.ScriptsRun);
        var version = await fixture.ScalarOnAsync<int>(SqlServerFixture.FreshClientName,
            "SELECT version FROM sm.moduleinfo WHERE module = N'settingsmanagement'");
        Assert.Equal(fixture.Scripts.Version, version);
    }

    [Fact]
    public async Task Install_OlderVersion_Upgrades()
    {
        var result = await fixture.Installer.InstallAsync(SqlServerFixture.UpgradeClientName);

        Assert.Equal("outdated", result.PreviousState);
        Assert.Equal(1, result.PreviousVersion);
        Assert.Equal(fixture.Scripts.Version, result.InstalledVersion);
    }

    [Fact]
    public async Task Install_UpToDate_RerunsAndKeepsVersion()
    {
        var result = await fixture.Installer.InstallAsync(SqlServerFixture.ClientName);

        Assert.Equal("up_to_date", result.PreviousState);
        Assert.Equal(result.InstalledVersion, result.PreviousVersion);
    }

    [Fact]
    public async Task Install_FailingScript_RollsBackAndNamesScript()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Installer.InstallAsync(SqlServerFixture.BrokenClientName));

        Assert.Equal("install_failed", ex.Code);
        Assert.Contains("030-procedure-GetProcesses.txt", ex.Message, StringComparison.Ordinal);
        Assert.Null(await fixture.ScalarOnAsync<int?>(SqlServerFixture.BrokenClientName, "SELECT OBJECT_ID(N'sm.moduleinfo')"));
        Assert.Null(await fixture.ScalarOnAsync<int?>(SqlServerFixture.BrokenClientName, "SELECT OBJECT_ID(N'sm.IsSafeName')"));
    }

    [Fact]
    public async Task Install_ConcurrentCalls_BothSucceed()
    {
        var results = await Task.WhenAll(
            fixture.Installer.InstallAsync(SqlServerFixture.ConcurrentClientName),
            fixture.Installer.InstallAsync(SqlServerFixture.ConcurrentClientName));

        Assert.All(results, r => Assert.Equal(fixture.Scripts.Version, r.InstalledVersion));
        Assert.Equal(1, await fixture.ScalarOnAsync<int>(SqlServerFixture.ConcurrentClientName, "SELECT COUNT(*) FROM sm.moduleinfo"));
    }

    [Fact]
    public async Task Install_DefaultMetaBase_ThrowsKeyNotFound() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Installer.InstallAsync(SqlConnectionParameters.DefaultMetaBaseClientName));
}

public sealed class ModuleStatesTests
{
    [Theory]
    [InlineData(null, 2, "not_installed")]
    [InlineData(1, 2, "outdated")]
    [InlineData(2, 2, "up_to_date")]
    [InlineData(3, 2, "newer_than_tool")]
    public void From_ClassifiesVersions(int? installed, int available, string expected) =>
        Assert.Equal(expected, SmModuleStates.From(installed, available));
}
