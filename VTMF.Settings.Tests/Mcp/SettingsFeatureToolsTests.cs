using VTMF.Settings.Services;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;
using VTMF.Settings.Mcp.Tools;
using VTMF.Settings.Tests.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Mcp;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SettingsFeatureToolsTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;

    private SettingsFeatureTools Tools => ToolsFor(fixture.FeatureStore);

    private SettingsFeatureTools ToolsFor(SmFeatureStore store) =>
        new(store, fixture.FeatureInstaller, new SmFeaturePlanner(fixture.Sm, store), fixture.Sm);

    private static string UniqueName() => "t-" + Guid.NewGuid().ToString("N")[..8];

    private static SmFeature Feature(string name, IReadOnlyList<SmFeatureParam>? ps = null, string row = "Grid.PageSize") => new()
    {
        Name = name,
        Description = "Lets the user page through documents",
        BuiltOnProcess = "x",
        Rows = [new SmFeatureRow { Table = "DocumentListLayout", Name = row, Value = "1" }],
        Params = ps ?? [],
    };

    private async Task<int> SaveFinishedAsync(SmFeature feature)
    {
        var saved = (await Tools.SaveFeature(feature, null)).Data!;
        var finished = await Tools.SetFeatureStatus(saved.Id, "Finished", saved.Revision);
        Assert.True(finished.Ok);
        return saved.Id;
    }

    [Fact]
    public async Task FeatureTools_SaveFindGetFinishPlan_Flow()
    {
        var name = UniqueName();
        var saved = await Tools.SaveFeature(Feature(name), null);
        Assert.True(saved.Ok);

        var drafts = await Tools.FindFeatures(name, "Draft");
        Assert.True(drafts.Ok);
        Assert.Contains(drafts.Data!, f => f.Id == saved.Data!.Id);
        Assert.Empty((await Tools.FindFeatures(name, null)).Data!);
        Assert.Single((await Tools.FindFeatures(name, "all")).Data!);

        var got = await Tools.GetFeature(saved.Data!.Id, null);
        Assert.Equal(name, got.Data!.Name);

        var finished = await Tools.SetFeatureStatus(saved.Data.Id, "Finished", got.Data.Revision!.Value);
        Assert.True(finished.Ok);

        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await fixture.Sm.StartSessionAsync(Client, "feature tools", p)).Id;
        var plan = await Tools.FeaturePlan(Client, saved.Data.Id, p, session, null, null);

        Assert.True(plan.Ok);
        Assert.Equal("Created", plan.Data!.Plan.Status);
        Assert.NotEmpty(plan.Data.Plan.Items);
    }

    [Fact]
    public async Task FeatureTools_Errors_AreEnvelopes()
    {
        var missing = await Tools.GetFeature(-1, null);
        Assert.False(missing.Ok);
        Assert.Equal("feature_not_found", missing.Error!.Code);

        var id = await SaveFinishedAsync(Feature(
            UniqueName(),
            [new SmFeatureParam { Name = "q", Description = "text", Type = "Text" }],
            row: "Grid.{{q}}"));
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await fixture.Sm.StartSessionAsync(Client, "feature tools", p)).Id;

        var plan = await Tools.FeaturePlan(Client, id, p, session, null, null);

        Assert.False(plan.Ok);
        Assert.Equal("feature_param_missing", plan.Error!.Code);
    }

    [Fact]
    public async Task FeatureTools_NoMetabase_ReturnsMetabaseNotConfigured()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.Connections.GetConnectionString(Client));
        var connections = new SqlConnectionProvider(new Dictionary<string, SqlConnectionParameters>
        {
            [Client] = new()
            {
                ClientName = Client,
                Server = $"Server={builder.DataSource};Database={builder.InitialCatalog};TrustServerCertificate=True",
                UserId = builder.UserID,
                Password = builder.Password,
            },
        });
        var tools = ToolsFor(new SmFeatureStore(connections, fixture.MetabaseScripts));

        var result = await tools.FindFeatures("x", "Finished");

        Assert.False(result.Ok);
        Assert.Equal("metabase_not_configured", result.Error!.Code);
    }

    [Fact]
    public async Task ListAppliedFeatures_JoinsNames()
    {
        var name = UniqueName();
        var id = await SaveFinishedAsync(Feature(name));
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = (await fixture.Sm.StartSessionAsync(Client, "feature tools", p)).Id;
        var plan = (await Tools.FeaturePlan(Client, id, p, session, null, null)).Data!;
        await fixture.Sm.ApplyPlanAsync(Client, plan.Plan.PlanId!.Value, plan.Plan.Checksum!);

        var listed = await Tools.ListAppliedFeatures(Client, p);

        Assert.True(listed.Ok);
        var info = Assert.Single(listed.Data!);
        Assert.Equal(id, info.Applied.FeatureId);
        Assert.Equal(name, info.CurrentName);
        Assert.Equal("Finished", info.Status);
        Assert.Equal("Lets the user page through documents", info.Description);

        var revision = (await Tools.GetFeature(id, null)).Data!.Revision!.Value;
        revision = (await Tools.SetFeatureStatus(id, "Draft", revision)).Data!.Revision;
        Assert.True((await Tools.DeleteFeature(id, revision)).Data);
        var afterDelete = Assert.Single((await Tools.ListAppliedFeatures(Client, p)).Data!);
        Assert.Null(afterDelete.CurrentName);
        Assert.Null(afterDelete.Status);
        Assert.Equal(name, afterDelete.Applied.FeatureName);
    }

    private SettingsFeatureTools CoexistTools() =>
        new(new SmFeatureStore(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.Coexist),
            new SmFeatureInstaller(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.Coexist),
            new SmFeaturePlanner(fixture.Sm, fixture.FeatureStore),
            fixture.Sm);

    private async Task WithStampedMetabaseVersionAsync(int version, Func<Task> body)
    {
        var module = fixture.MetabaseScripts.Set.ModuleName;
        var connection = fixture.Connections.GetConnectionString(SqlServerFixture.Coexist);
        var original = fixture.MetabaseScripts.Set.Version;
        await new SmFeatureInstaller(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.Coexist).InstallAsync();
        await ExecuteAsync(connection, $"UPDATE sm.moduleinfo SET version = {version} WHERE module = N'{module}'");
        try
        {
            await body();
        }
        finally
        {
            await ExecuteAsync(connection, $"UPDATE sm.moduleinfo SET version = {original} WHERE module = N'{module}'");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task FeatureTools_ModuleNotInstalled_ReturnsFeatureModuleNotInstalled()
    {
        var tools = ToolsFor(new SmFeatureStore(fixture.Connections, fixture.MetabaseScripts, SqlServerFixture.EmptyClientName));

        var result = await tools.FindFeatures("x", "Finished");

        Assert.False(result.Ok);
        Assert.Equal("feature_module_not_installed", result.Error!.Code);
    }

    [Fact]
    public async Task FeatureTools_ModuleOutdated_ReturnsFeatureModuleOutdated()
    {
        await WithStampedMetabaseVersionAsync(0, async () =>
        {
            var result = await CoexistTools().FindFeatures("x", "Finished");

            Assert.False(result.Ok);
            Assert.Equal("feature_module_outdated", result.Error!.Code);
        });
    }

    [Fact]
    public async Task FeatureTools_ModuleNewer_ReturnsFeatureModuleNewerThanTool_AlsoOnInstall()
    {
        await WithStampedMetabaseVersionAsync(fixture.MetabaseScripts.Set.Version + 1, async () =>
        {
            var tools = CoexistTools();

            var found = await tools.FindFeatures("x", "Finished");
            var installed = await tools.InstallFeatureModule();

            Assert.Equal("feature_module_newer_than_tool", found.Error!.Code);
            Assert.False(installed.Ok);
            Assert.Equal("feature_module_newer_than_tool", installed.Error!.Code);
        });
    }

    [Fact]
    public async Task ClientTools_OutdatedClient_ReturnModuleOutdated()
    {
        var applied = await Tools.ListAppliedFeatures(SqlServerFixture.OutdatedClientName);
        var plan = await Tools.FeaturePlan(SqlServerFixture.OutdatedClientName, await SaveFinishedAsync(Feature(UniqueName())), "tproc", 1);

        Assert.False(applied.Ok);
        Assert.Equal("module_outdated", applied.Error!.Code);
        Assert.False(plan.Ok);
        Assert.Equal("module_outdated", plan.Error!.Code);
    }

    [Fact]
    public async Task SaveFeature_NullCollections_AreTreatedAsEmpty()
    {
        var feature = new SmFeature { Name = UniqueName(), Rows = null!, Params = null!, Steps = null! };

        var saved = await Tools.SaveFeature(feature, null);

        Assert.True(saved.Ok);
        var got = (await Tools.GetFeature(saved.Data!.Id, null)).Data!;
        Assert.Empty(got.Rows);
        Assert.Empty(got.Params);
        Assert.Empty(got.Steps);
    }

    [Fact]
    public async Task SaveFeature_NullParamValues_AreTreatedAsEmpty()
    {
        var feature = new SmFeature
        {
            Name = UniqueName(),
            Params = [new SmFeatureParam { Name = "q", Description = "d", Type = "Text", Values = null! }],
        };

        var saved = await Tools.SaveFeature(feature, null);

        Assert.True(saved.Ok);
    }
}
