using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsReadToolsTests(SqlServerFixture fixture)
{
    private SettingsReadTools Tools => new(fixture.Sm, fixture.Connections);

    [Fact]
    public void ListClients_ReturnsConfiguredClients()
    {
        var result = Tools.ListClients();

        Assert.True(result.Ok);
        Assert.Equal(SqlServerFixture.AllClientNames.Order(StringComparer.Ordinal), result.Data!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ModuleStatus_ClientWithoutModule_IsOkWithInstalledFalse()
    {
        var result = await Tools.ModuleStatus(SqlServerFixture.EmptyClientName, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(result.Data!.Installed);
    }

    [Fact]
    public async Task ListProcesses_ClientWithoutModule_ReturnsModuleNotInstalledError()
    {
        var result = await Tools.ListProcesses(SqlServerFixture.EmptyClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.ModuleNotInstalled, result.Error!.Code);
    }

    [Fact]
    public async Task GetSettings_UnknownClient_ReturnsUnknownClientError()
    {
        var result = await Tools.GetSettings("nosuchclient", "tproc", "DocumentListLayout", "base", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownClient, result.Error!.Code);
    }

    [Fact]
    public async Task GetSettings_InvalidLayer_ReturnsCodedError()
    {
        var result = await Tools.GetSettings(SqlServerFixture.ClientName, "tproc", "DocumentListLayout", "worker:bob", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.InvalidLayer, result.Error!.Code);
    }

    [Fact]
    public async Task GetEffectiveSettings_ReturnsRows()
    {
        var result = await Tools.GetEffectiveSettings(SqlServerFixture.ClientName, "tproc", "DocumentListLayout", "wterminal2", "en", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(10, result.Data!.Rows.Count);
    }

    [Fact]
    public async Task SettingsUsage_And_LegacyEnvRows_AreOk()
    {
        var usage = await Tools.SettingsUsage(SqlServerFixture.ClientName, "tproc", null, CancellationToken.None);
        var legacy = await Tools.LegacyEnvRows(SqlServerFixture.ClientName, "tproc", CancellationToken.None);
        var tables = await Tools.ListTables(SqlServerFixture.ClientName, "tproc", CancellationToken.None);

        Assert.True(usage.Ok && legacy.Ok && tables.Ok);
    }

    [Fact]
    public async Task ModuleStatus_UnreachableServer_ReturnsSmError()
    {
        var result = await Tools.ModuleStatus(SqlServerFixture.UnreachableClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.SmError, result.Error!.Code);
    }

    [Fact]
    public async Task ListProcesses_UnreachableServer_ReturnsSmError()
    {
        var result = await Tools.ListProcesses(SqlServerFixture.UnreachableClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.SmError, result.Error!.Code);
    }
}
