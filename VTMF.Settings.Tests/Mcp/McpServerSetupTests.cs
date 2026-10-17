using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using VTMF.Settings.Mcp;

namespace VTMF.Settings.Tests.Mcp;

public sealed class McpServerSetupTests
{
    [Fact]
    public void AddSettingsMcpServer_SetsInstructions()
    {
        var services = new ServiceCollection();
        services.AddSettingsMcpServer();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(McpServerSetup.Instructions, options.ServerInstructions);
    }
}
