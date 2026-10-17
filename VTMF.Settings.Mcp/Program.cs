using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Services;

var transport = McpTransportOption.Parse(args);

if (transport == McpTransport.Http)
{
    var builder = WebApplication.CreateBuilder(args);
    AddConfiguration(builder.Configuration);
    builder.Services.AddSqlConnectionParameters().AddSmClient();
    builder.Services.AddSettingsMcpServer().WithHttpTransport();

    var app = builder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync().ConfigureAwait(false);
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    // stdout is the MCP channel in stdio mode: all logs go to stderr.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    AddConfiguration(builder.Configuration);
    builder.Services.AddSqlConnectionParameters().AddSmClient();
    builder.Services.AddSettingsMcpServer().WithStdioServerTransport();

    await builder.Build().RunAsync().ConfigureAwait(false);
}

static void AddConfiguration(IConfigurationBuilder configuration)
{
    // Same sources as VTMF.Settings: user secrets (developer machines) and environment variables
    // (Docker / settings.env). Environment variables win.
    configuration.AddUserSecrets(typeof(SettingsReadTools).Assembly, optional: true);
    configuration.AddEnvironmentVariables();
}
