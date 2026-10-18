using VTMF.Settings.Core.Sm.Usage;

namespace VTMF.Settings.Tests.Sm;

public sealed class SqlLiteralTests
{
    [Theory]
    [InlineData("'abc'", "abc")]
    [InlineData("N'abc'", "abc")]
    [InlineData("'it''s'", "it's")]
    [InlineData("  'x'  ", "x")]
    [InlineData(" 5 ", "5")]
    [InlineData("@x", "@x")]
    [InlineData("'", "'")]
    [InlineData("5\r\n", "5")]
    [InlineData(null, null)]
    public void Unquote_MatchesSqlFunction(string? input, string? expected)
        => Assert.Equal(expected, SqlLiteral.Unquote(input));
}
