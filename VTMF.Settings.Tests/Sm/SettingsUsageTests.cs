using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsUsageTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.ClientName;

    [Fact]
    public async Task Usage_ReportsPsvLegacyDirectUnparsedAndUnused()
    {
        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tproc");

        Assert.Equal(
            [
                ("Directo.ApiKey", null, "none", null, "********", true),
                ("Document.Query.Join", "GetDocumentsFortproc", "psv", "False", "True", false),
                ("Document.RegDocState.RemoveNoQty", "RegDocStateFortprocFinished", "legacy", "False", null, false),
                ("Document.State.Terminal.New", "GetDocumentsFortproc", "psv", "UUS", "NEW", false),
                ("Unused.Setting", null, "none", null, "1", true),
                (null, "GetDocumentsFortproc", "unparsed", null, null, false),
                (null, "RegDocStateFortprocFinished", "direct", null, null, false),
            ],
            usage.Select(u => (u.SettingName, u.ProcedureName, u.CallStyle, u.DefaultInCode, u.ConfiguredValue, u.Unused)).ToArray());
        Assert.Equal(1, usage.Single(u => u.SettingName == "Document.Query.Join").WorkerOverrides);
        Assert.Equal("1", usage.Single(u => u.CallStyle == "unparsed").Detail);
        Assert.Equal("ServerProcessSettings", usage.Single(u => u.CallStyle == "direct").Detail);
        Assert.DoesNotContain(usage, u => u.ProcedureName == "GetDocumentsFortother");
    }

    [Fact]
    public async Task Usage_AliasProcess_IncludesParentReadsAndResolvesConfiguredValues()
    {
        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tprocvariant", "Document.");

        var newState = usage.Single(u => u.SettingName == "Document.State.Terminal.New");
        Assert.Equal(("VNEW", "process"), (newState.ConfiguredValue, newState.ConfiguredIn));
        var join = usage.Single(u => u.SettingName == "Document.Query.Join");
        Assert.Equal(("True", "parent", 0), (join.ConfiguredValue, join.ConfiguredIn, join.WorkerOverrides));
        Assert.All(usage, u => Assert.StartsWith("Document.", u.SettingName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_RunsAtCompatibilityLevel150()
    {
        await fixture.ExecuteOnTestAsync("IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) <> 150 THROW 50000, 'fixture must run at level 150', 1;");

        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tproc");

        Assert.NotEmpty(usage);
    }
}
