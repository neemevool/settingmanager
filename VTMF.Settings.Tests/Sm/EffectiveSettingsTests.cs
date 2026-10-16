using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class EffectiveSettingsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task Layout_WithEnvironmentAndLanguage_MatchesWsReader()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "wterminal2", "en");

        Assert.Equal("tprocDocumentListLayout", result.Info.PhysicalTable);
        Assert.False(result.Info.AliasUsed);
        Assert.Null(result.Info.OverrideProcedure);
        Assert.Equal(
            [
                ("Column", "DOCNR", "base"),
                ("Column.DOCNR.Caption", "Doc no (env)", "env"),
                ("Column", "DATE", "base"),
                ("Column.DATE.Caption", "Date", "lang"),
                ("Grid.PageSize", "20", "base"),
                ("Grid.Secret", "********", "base"),
                ("Column", "STATUS", "env"),
                ("Grid.ShowFilterRow", "true", "env"),
                ("Grid.ShowFilterRow", "false", "lang"),
                ("Lang.Only", "x", "lang"),
            ],
            result.Rows.Select(r => (r.Name, r.Value, r.Source)).ToArray());
    }

    [Fact]
    public async Task Layout_DefaultEnvironmentAndNoLanguage_ReturnsBaseOnly()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "wterminal", null);

        Assert.Equal(6, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal("base", r.Source));
        Assert.Equal("Dok nr", result.Rows.Single(r => r.Name == "Column.DOCNR.Caption").Value);
    }

    [Fact]
    public async Task Layout_AliasProcess_UsesParentTable()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tprocvariant", "DocumentListLayout");

        Assert.True(result.Info.AliasUsed);
        Assert.Equal("tprocDocumentListLayout", result.Info.PhysicalTable);
        Assert.Equal(6, result.Rows.Count);
    }

    [Fact]
    public async Task Layout_WithActiveInfoProcedure_ReportsOverride()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentItemsListLayout");

        Assert.Equal("GettprocDocumentItemsListLayoutInfo", result.Info.OverrideProcedure);
        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task Backend_AliasProcess_FallsBackToParentAndMasksSecrets()
    {
        var result = await fixture.Sm.GetEffectiveSettingsAsync(Client, "tprocvariant", "ServerProcessSettings");

        Assert.Equal(
            [
                ("Directo.ApiKey", "********", "parent"),
                ("Document.Query.Join", "True", "parent"),
                ("Document.State.Terminal.New", "VNEW", "process"),
                ("Unused.Setting", "1", "parent"),
            ],
            result.Rows.Select(r => (r.Name, r.Value, r.Source)).ToArray());
    }

    [Fact]
    public async Task UnsafeEnvironment_ThrowsInvalidLayer()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.GetEffectiveSettingsAsync(Client, "tproc", "DocumentListLayout", "x;drop"));

        Assert.Equal(SmErrorCodes.InvalidLayer, ex.Code);
    }
}
