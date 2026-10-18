using System.Globalization;
using Microsoft.Data.SqlClient;
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
    public async Task Usage_EmptyChildValueHidesParentForPsvButLegacyReadsMappedProcess()
    {
        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tprocvariant", "Unused.");

        Assert.Equal(
            [
                ("Unused.Setting", "GetVariantExtras", "legacy", "legacydflt", "1", "parent", 0),
                ("Unused.Setting", "GetVariantExtras", "psv", "dflt", null, null, 0),
            ],
            usage.Select(u => (u.SettingName, u.ProcedureName, u.CallStyle, u.DefaultInCode, u.ConfiguredValue, u.ConfiguredIn, u.WorkerOverrides)).ToArray());
    }

    [Fact]
    public async Task Usage_RunsAtCompatibilityLevel150()
    {
        await fixture.ExecuteOnTestAsync("IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) <> 150 THROW 50000, 'fixture must run at level 150', 1;");

        var usage = await fixture.Sm.GetSettingsUsageAsync(Client, "tproc");

        Assert.NotEmpty(usage);
    }

    [Fact]
    public async Task Procedure_NeverReturnsServerSettingValue()
    {
        await using var connection = new SqlConnection(fixture.Connections.GetConnectionString(Client));
        await connection.OpenAsync();
        await using var command = new SqlCommand("EXEC sm.GetSettingsUsage @process = N'tproc'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        var resultSets = 0;
        do
        {
            resultSets++;
            while (await reader.ReadAsync())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    values.Add(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }
        while (await reader.NextResultAsync());

        Assert.DoesNotContain("secret-key", values);
        Assert.Contains("********", values);
        Assert.Equal(3, resultSets);
    }
}
