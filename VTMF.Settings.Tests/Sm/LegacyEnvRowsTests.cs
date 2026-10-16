using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class LegacyEnvRowsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task LegacyEnvRows_FindsEnvironmentRowsInBaseTables()
    {
        var rows = await fixture.Sm.GetLegacyEnvRowsAsync(SqlServerFixture.ClientName, "tproc");

        var row = Assert.Single(rows);
        Assert.Equal(
            ("tprocDocumentListLayout", "DocumentListLayout", "wterminal2", "Column.DOCNR.Caption", "Legacy", true),
            (row.PhysicalTable, row.View, row.Environment, row.Name, row.Value, row.OverlayExists));
    }

    [Fact]
    public async Task LegacyEnvRows_MasksServerSettings()
    {
        await fixture.ExecuteOnTestAsync(
            "INSERT INTO dbo.totherProcessSettings VALUES (N'Api.Key', N'plain-secret', N'ServerSetting', NULL, N'wterminal3', 20);");

        var rows = await fixture.Sm.GetLegacyEnvRowsAsync(SqlServerFixture.ClientName, "tother");

        var row = Assert.Single(rows);
        Assert.Equal(("********", false), (row.Value, row.OverlayExists));
    }
}
