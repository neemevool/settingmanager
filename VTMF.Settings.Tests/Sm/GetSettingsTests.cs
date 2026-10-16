using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class GetSettingsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task BackendTable_StripsPrefixExcludesWorkersAndMasksSecrets()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "ServerProcessSettings");

        Assert.Equal(
            [("Directo.ApiKey", "********"), ("Document.Query.Join", "True"), ("Document.State.Terminal.New", "NEW"), ("Unused.Setting", "1")],
            rows.Select(r => (r.Name, r.Value)).ToArray());
    }

    [Fact]
    public async Task BaseLayer_ReturnsAllRowsInOrderIncludingEmptyValues()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout");

        Assert.Equal(
            ["Column", "Column.DOCNR.Caption", "Column", "Column.DATE.Caption", "Grid.PageSize", "Grid.Empty", "Grid.Secret"],
            rows.Select(r => r.Name).ToArray());
        Assert.Equal("********", rows.Single(r => r.Name == "Grid.Secret").Value);
        Assert.All(rows, r => Assert.Equal("base", r.Layer));
    }

    [Theory]
    [InlineData("env:wterminal2", 4)]
    [InlineData("lang:en", 3)]
    public async Task OverlayLayers_ReadOverlayTablesWithLayerWorkercode(string layer, int expectedCount)
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout", layer);

        Assert.Equal(expectedCount, rows.Count);
        Assert.All(rows, r => Assert.Equal(layer, r.Layer));
    }

    [Fact]
    public async Task NamePrefix_FiltersRows()
    {
        var rows = await fixture.Sm.GetSettingsAsync(Client, "tproc", "DocumentListLayout", namePrefix: "Column.");

        Assert.Equal(["Column.DOCNR.Caption", "Column.DATE.Caption"], rows.Select(r => r.Name).ToArray());
    }

    [Theory]
    [InlineData("tproc", "DocumentListLayout", "env:Bad;", SmErrorCodes.InvalidLayer)]
    [InlineData("tproc", "DocumentListLayout", "worker:bob", SmErrorCodes.InvalidLayer)]
    [InlineData("tproc", "DocumentListLayout", "env:WTERMINAL2", SmErrorCodes.InvalidLayer)]
    [InlineData("x;drop", "DocumentListLayout", "base", SmErrorCodes.InvalidName)]
    [InlineData("tproc", "Document ListLayout", "base", SmErrorCodes.InvalidName)]
    [InlineData("nosuch", "DocumentListLayout", "base", SmErrorCodes.ProcessNotFound)]
    [InlineData("tproc", "Nope", "base", SmErrorCodes.TableNotFound)]
    [InlineData("tproc", "ServerProcessSettings", "env:wterminal2", SmErrorCodes.LayerNotSupported)]
    public async Task InvalidInput_ThrowsCodedError(string process, string table, string layer, string expectedCode)
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetSettingsAsync(Client, process, table, layer));

        Assert.Equal(expectedCode, ex.Code);
    }
}
