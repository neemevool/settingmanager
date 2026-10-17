using VTMF.Settings.Core.Sm;
using VTMF.Settings.Mcp;

namespace VTMF.Settings.Tests.Mcp;

public sealed class ToolResultTests
{
    [Fact]
    public async Task RunAsync_UnexpectedException_ReturnsSmError()
    {
        var result = await ToolResult.RunAsync<int>(() => throw new InvalidOperationException("boom"));

        Assert.False(result.Ok);
        Assert.Equal(SmErrorCodes.SmError, result.Error!.Code);
        Assert.Equal("boom", result.Error.Message);
    }

    [Fact]
    public async Task RunAsync_Cancellation_Propagates() =>
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ToolResult.RunAsync<int>(() => throw new OperationCanceledException()));
}
