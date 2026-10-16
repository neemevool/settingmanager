using VTMF.Settings.Mcp;

namespace VTMF.Settings.Tests.Mcp;

public sealed class McpTransportOptionTests
{
    [Theory]
    [InlineData(new string[0], McpTransport.Stdio)]
    [InlineData(new[] { "--transport", "stdio" }, McpTransport.Stdio)]
    [InlineData(new[] { "--transport", "http", "--urls", "http://0.0.0.0:8080" }, McpTransport.Http)]
    [InlineData(new[] { "--transport=HTTP" }, McpTransport.Http)]
    public void Parse_ReturnsTransport(string[] args, McpTransport expected) =>
        Assert.Equal(expected, McpTransportOption.Parse(args));

    [Fact]
    public void Parse_UnknownTransport_Throws() =>
        Assert.Throws<ArgumentException>(() => McpTransportOption.Parse(["--transport", "pigeon"]));
}
