using VTMF.Settings.Core.Sm.Usage;

namespace VTMF.Settings.Tests.Sm;

public sealed class SettingsUsageScannerTests
{
    [Fact]
    public void Scan_SeedGetDocumentsFortproc()
    {
        const string body = """
            CREATE PROCEDURE dbo.GetDocumentsFortproc @workercode nvarchar(250)
            AS
            BEGIN
                declare @psv dbo.processSettings
                insert into @psv select * from dbo.GetProcessSettingValues('tproc', @workercode)
                declare @new nvarchar(30) = dbo.GetPSV(@psv, 'Document.State.Terminal.New', 'UUS')
                declare @join nvarchar(10) = dbo.GetPSVB(@psv,'Document.Query.Join','False')
                declare @dyn nvarchar(30) = dbo.GetPSV(@psv, @new, 'x')
                select @new, @join, @dyn
            END
            """;
        var r = SettingsUsageScanner.Scan(body);
        Assert.Equal("tproc", r.LiteralProcess);
        Assert.Equal(
            [new SmPsvRead("Document.State.Terminal.New", "'UUS'"), new SmPsvRead("Document.Query.Join", "'False'")],
            r.PsvReads);
        Assert.Equal(1, r.Unparsed);
        Assert.False(r.HasDirectRead);
    }

    [Fact]
    public void Scan_SeedRegDocState()
    {
        const string body = """
            CREATE PROCEDURE dbo.RegDocStateFortprocFinished @workercode nvarchar(250)
            AS
            BEGIN
                declare @remove nvarchar(10) = dbo.GetProcessSettingValue('tproc','Document.RegDocState.RemoveNoQty',@workercode,'False')
                declare @key nvarchar(400) = (select top 1 VALUE from dbo.ServerProcessSettings where NAME = 'tproc.Directo.ApiKey')
                select @remove, @key
            END
            """;
        var r = SettingsUsageScanner.Scan(body);
        Assert.Equal([new SmLegacyRead("'tproc'", "Document.RegDocState.RemoveNoQty", "'False'")], r.LegacyReads);
        Assert.True(r.HasDirectRead);
        Assert.Null(r.LiteralProcess);
        Assert.Equal(0, r.Unparsed);
    }

    [Fact]
    public void Scan_LegacyVariableProcessAndTypedVariants()
    {
        var r = SettingsUsageScanner.Scan("select dbo.GetProcessSettingValueInt(@p, N'A.B', @w, 5)");
        Assert.Equal([new SmLegacyRead("@p", "A.B", "5")], r.LegacyReads);

        var d = SettingsUsageScanner.Scan("select dbo.GetProcessSettingValueDec(@p, @n, @w, 5)");
        Assert.Empty(d.LegacyReads);
        Assert.Equal(1, d.Unparsed);
    }

    [Fact]
    public void Scan_PsvDefaultWithEscapedQuoteAndNPrefix()
    {
        var r = SettingsUsageScanner.Scan("select dbo.GetPSV(@psv, N'X', N'it''s')");
        Assert.Equal([new SmPsvRead("X", "N'it''s'")], r.PsvReads);
    }

    [Fact]
    public void Scan_CallSpanningLines()
    {
        var r = SettingsUsageScanner.Scan("select dbo.GetPSV(\r\n  @psv,\r\n  'A.B',\r\n  'd'\r\n)");
        var read = Assert.Single(r.PsvReads);
        Assert.Equal("A.B", read.SettingName);
        // Raw default keeps trailing whitespace (as the SQL regex did); unquoting trims it.
        Assert.Equal("'d'\r\n", read.DefaultRaw);
        Assert.Equal("d", SqlLiteral.Unquote(read.DefaultRaw));
        Assert.Equal(0, r.Unparsed);
    }

    [Fact]
    public void Scan_IsCaseInsensitive()
    {
        var r = SettingsUsageScanner.Scan(
            "select dbo.getpsvb(@psv,'A','b') from DBO.serverprocesssettings " +
            "insert @t select * from getprocesssettingvalues(N'TPROC', @w)");
        Assert.Equal([new SmPsvRead("A", "'b'")], r.PsvReads);
        Assert.True(r.HasDirectRead);
        Assert.Equal("TPROC", r.LiteralProcess);
    }

    [Fact]
    public void Scan_GetProcessSettingValuesIsNotALegacyCall()
    {
        var r = SettingsUsageScanner.Scan("select * from GetProcessSettingValues('tproc', @w)");
        Assert.Empty(r.LegacyReads);
        Assert.Equal(0, r.Unparsed);
    }
}
