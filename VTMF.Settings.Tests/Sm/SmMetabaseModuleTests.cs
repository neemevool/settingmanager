using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SmMetabaseModuleTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task FeatureModule_InstalledInMetabase_IsUpToDate()
    {
        var status = await fixture.FeatureStore.GetModuleStatusAsync();

        Assert.Equal("up_to_date", status.State);
        Assert.Equal(1, status.Version);
    }

    [Fact]
    public async Task FeatureModule_Reinstall_IsIdempotent()
    {
        var result = await fixture.FeatureInstaller.InstallAsync();

        Assert.Equal(1, result.PreviousVersion);
        Assert.Equal(1, result.InstalledVersion);
    }

    [Fact]
    public async Task FeatureModule_CoexistsWithClientModule()
    {
        await new SmFeatureInstaller(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.Coexist).InstallAsync();

        var client = await fixture.Sm.GetModuleStatusAsync(SqlServerFixture.Coexist);
        var features = await new SmFeatureStore(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.Coexist).GetModuleStatusAsync();

        Assert.Equal("up_to_date", client.State);
        Assert.Equal("up_to_date", features.State);
    }

    [Fact]
    public async Task FeatureModule_NotInstalled_ReportsNotInstalled()
    {
        var status = await new SmFeatureStore(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.EmptyClientName).GetModuleStatusAsync();

        Assert.Equal("not_installed", status.State);
        Assert.False(status.Installed);
    }
}
