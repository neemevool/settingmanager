using VTMF.Settings.Core.Sm;

namespace VTMF.Settings.Tests.Sm;

public sealed class SmErrorCodesTests
{
    [Theory]
    [InlineData(51006, "invalid_desired")]
    [InlineData(51007, "serversetting")]
    [InlineData(51008, "worker_layer")]
    [InlineData(51009, "alias_target")]
    [InlineData(51010, "table_missing")]
    [InlineData(51011, "duplicate_row")]
    [InlineData(51012, "session_not_found")]
    [InlineData(51013, "session_closed")]
    [InlineData(51014, "plan_not_found")]
    [InlineData(51015, "plan_status")]
    [InlineData(51016, "checksum_mismatch")]
    public void FromSqlErrorNumber_MapsPlanEngineErrors(int number, string code) =>
        Assert.Equal(code, SmErrorCodes.FromSqlErrorNumber(number));
}
