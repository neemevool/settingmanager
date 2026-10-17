using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Services;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsModuleToolsTests(SqlServerFixture fixture)
{
    private SettingsModuleTools Tools => new(fixture.Installer);

    [Fact]
    public async Task InstallModule_FreshClient_ReturnsResult()
    {
        var result = await Tools.InstallModule(SqlServerFixture.ToolFreshClientName, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(fixture.Scripts.Version, result.Data!.InstalledVersion);

        var processes = await new SettingsReadTools(fixture.Sm, fixture.Connections)
            .ListProcesses(SqlServerFixture.ToolFreshClientName, CancellationToken.None);
        Assert.True(processes.Ok);
    }

    [Fact]
    public async Task InstallModule_Broken_ReturnsInstallFailed()
    {
        var result = await Tools.InstallModule(SqlServerFixture.BrokenClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.InstallFailed, result.Error!.Code);
        Assert.Contains("030-procedure-GetProcesses.txt", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallModule_Newer_ReturnsNewerThanTool()
    {
        var result = await Tools.InstallModule(SqlServerFixture.NewerClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.ModuleNewerThanTool, result.Error!.Code);
    }

    [Fact]
    public async Task InstallModule_UnknownClient_ReturnsUnknownClient()
    {
        var result = await Tools.InstallModule("nosuchclient", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownClient, result.Error!.Code);
    }

    [Fact]
    public async Task InstallModule_DefaultMetaBase_ReturnsUnknownClient()
    {
        var result = await Tools.InstallModule(SqlConnectionParameters.DefaultMetaBaseClientName, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownClient, result.Error!.Code);
    }
}
