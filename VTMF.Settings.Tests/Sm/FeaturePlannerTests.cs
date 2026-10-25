using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;
using VTMF.Settings.Services;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class FeaturePlannerTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;

    private SmFeaturePlanner Planner => new(fixture.Sm, fixture.FeatureStore);

    private static string UniqueName() => "t-" + Guid.NewGuid().ToString("N")[..8];

    private async Task<int> SaveAsync(SmFeature feature, bool finish = true)
    {
        var saved = await fixture.FeatureStore.SaveFeatureAsync(feature, null);
        if (finish)
        {
            await fixture.FeatureStore.SetFeatureStatusAsync(saved.Id, "Finished", saved.Revision);
        }

        return saved.Id;
    }

    private static SmFeature Feature(string name, string table, string rowName, IReadOnlyList<SmFeatureParam>? ps = null,
        IReadOnlyList<SmFeatureStep>? steps = null) => new()
    {
        Name = name,
        Description = "Test feature",
        BuiltOnProcess = "x",
        Rows = [new SmFeatureRow { Table = table, Name = rowName, Value = "1" }],
        Params = ps ?? [],
        Steps = steps ?? [],
    };

    private async Task<(string P, int Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session.Id);
    }

    [Fact]
    public async Task FeaturePlan_SameFeatureTwoProcesses()
    {
        var id = await SaveAsync(Feature(UniqueName(), "DocumentListLayout", "Grid.{{processname}}"));
        foreach (var _ in new[] { 1, 2 })
        {
            var (p, session) = await NewProcessAsync();

            var result = await Planner.FeaturePlanAsync(Client, id, p, session, null);

            Assert.Equal("Created", result.Plan.Status);
            Assert.All(result.Plan.Items, i => Assert.Equal($"{p}DocumentListLayout", i.PhysicalTable));
            Assert.Contains(result.Plan.Items, i => i.Name == $"Grid.{p}");
        }
    }

    [Fact]
    public async Task FeaturePlan_SourceRecorded()
    {
        var name = UniqueName();
        var id = await SaveAsync(Feature(name, "DocumentListLayout", "Grid.PageSize"));
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, null);
        await fixture.Sm.ApplyPlanAsync(Client, result.Plan.PlanId!.Value, result.Plan.Checksum!);

        var applied = Assert.Single(await fixture.Sm.GetAppliedFeaturesAsync(Client, p));
        Assert.Equal(id, applied.FeatureId);
        Assert.Equal(name, applied.FeatureName);
    }

    [Fact]
    public async Task FeaturePlan_Draft_IsRefused()
    {
        var id = await SaveAsync(Feature(UniqueName(), "DocumentListLayout", "Grid.PageSize"), finish: false);
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => Planner.FeaturePlanAsync(Client, id, p, session, null));

        Assert.Equal(SmErrorCodes.FeatureNotFinished, ex.Code);
    }

    [Fact]
    public async Task FeaturePlan_UnreadServerSetting_Warns()
    {
        var id = await SaveAsync(Feature(UniqueName(), "ServerProcessSettings", "Feature.Unread"));
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, null);

        var warning = Assert.Single(result.Warnings, w => w.Code == "SETTING_NOT_READ");
        Assert.Equal("Warning", warning.Severity);
        Assert.Contains("'Feature.Unread'", warning.Message);
    }

    [Fact]
    public async Task FeaturePlan_ReadServerSetting_NoWarning()
    {
        var id = await SaveAsync(Feature(UniqueName(), "ServerProcessSettings", "Feature.Read"));
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            CREATE OR ALTER PROCEDURE dbo.Get{p}FeatureProbe @workercode nvarchar(250)
            AS
            BEGIN
                declare @v nvarchar(10) = dbo.GetProcessSettingValue('{p}','Feature.Read',@workercode,'False')
                select @v
            END
            """);

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, null);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SETTING_NOT_READ");
    }

    // Brief expected invalid_name here, but the engine validates only identifiers (process/table); a row NAME is data
    // passed as a JSON/SQL parameter, so the unsafe answer is carried literally and nothing is executed.
    [Fact]
    public async Task FeaturePlan_UnsafeAnswer_IsCarriedAsDataNotExecuted()
    {
        var feature = Feature(UniqueName(), "DocumentListLayout", "Grid.{{name}}",
            [new SmFeatureParam { Name = "name", Description = "a name", Type = "Text" }]);
        var id = await SaveAsync(feature);
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(
            Client, id, p, session, new Dictionary<string, string> { ["name"] = "LOT'; DROP" });
        await fixture.Sm.ApplyPlanAsync(Client, result.Plan.PlanId!.Value, result.Plan.Checksum!);

        Assert.Contains(result.Plan.Items, i => i.Name == "Grid.LOT'; DROP");
        var rows = await PlanSandbox.RowsAsync(fixture, Client, $"{p}DocumentListLayout");
        Assert.Contains(rows, r => r.Name == "Grid.LOT'; DROP");
        Assert.Contains(rows, r => r.Name == "Grid.PageSize");
    }

    private static SmFeature BarcodeColumn(string name) => new()
    {
        Name = name,
        Description = "Adds a barcode column",
        BuiltOnProcess = "x",
        Rows =
        [
            new SmFeatureRow { Table = "DocumentListLayout", Name = "Column", Value = "BARCODE", SettingType = "SettingGroup", After = "{{pos}}" },
            new SmFeatureRow { Table = "DocumentListLayout", Name = "Column.BARCODE.Caption", Value = "Barcode" },
        ],
        Params = [new SmFeatureParam { Name = "pos", Description = "After which column should the barcode column be shown? (or first)", Type = "Position" }],
    };

    private static Dictionary<string, string> Pos(string answer) => new() { ["pos"] = answer };

    [Fact]
    public async Task FeaturePlan_Position_PlacesColumnBlock()
    {
        var id = await SaveAsync(BarcodeColumn(UniqueName()));
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, Pos("DOCNR"));

        var group = Assert.Single(result.Plan.Items, i => i.Name == "Column" && i.Value == "BARCODE");
        var caption = Assert.Single(result.Plan.Items, i => i.Name == "Column.BARCODE.Caption");
        Assert.Equal(23, group.OrderNr);
        Assert.Equal(26, caption.OrderNr);
    }

    [Fact]
    public async Task FeaturePlan_PositionFirst_PlacesColumnBlockFirst()
    {
        var id = await SaveAsync(BarcodeColumn(UniqueName()));
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, Pos("first"));

        var group = Assert.Single(result.Plan.Items, i => i.Name == "Column" && i.Value == "BARCODE");
        var caption = Assert.Single(result.Plan.Items, i => i.Name == "Column.BARCODE.Caption");
        Assert.True(group.OrderNr < caption.OrderNr);
        Assert.True(caption.OrderNr < 10);
    }

    [Fact]
    public async Task FeaturePlan_PositionMissingColumn_Refused()
    {
        var id = await SaveAsync(BarcodeColumn(UniqueName()));
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => Planner.FeaturePlanAsync(Client, id, p, session, Pos("NOPE")));

        Assert.Equal(SmErrorCodes.GroupAnchorNotFound, ex.Code);
    }

    [Fact]
    public async Task FeaturePlan_Reapplied_GroupNotMoved()
    {
        var id = await SaveAsync(BarcodeColumn(UniqueName()));
        var (p, session) = await NewProcessAsync();
        var first = await Planner.FeaturePlanAsync(Client, id, p, session, Pos("DOCNR"));
        await fixture.Sm.ApplyPlanAsync(Client, first.Plan.PlanId!.Value, first.Plan.Checksum!);

        var again = await Planner.FeaturePlanAsync(Client, id, p, session, Pos("NOPE"));

        Assert.Contains(again.Plan.Warnings, w => w.Code == "GROUP_NOT_MOVED");
        var rows = await PlanSandbox.RowsAsync(fixture, Client, $"{p}DocumentListLayout");
        Assert.Equal(23, Assert.Single(rows, r => r.Name == "Column" && r.Value == "BARCODE").OrderNr);
    }

    [Fact]
    public async Task FeaturePlan_OldFinishedFeatureWithoutPosition_Refused()
    {
        var feature = BarcodeColumn(UniqueName());
        var old = new SmFeature
        {
            Name = feature.Name,
            Description = feature.Description,
            BuiltOnProcess = feature.BuiltOnProcess,
            Rows = [new SmFeatureRow { Table = "DocumentListLayout", Name = "Column", Value = "BARCODE", SettingType = "SettingGroup" }, feature.Rows[1]],
        };
        var id = await SaveAsync(old, finish: false);
        await PlanSandbox.ExecAsync(fixture, SqlConnectionParameters.DefaultMetaBaseClientName,
            $"UPDATE sm.feature SET status = N'Finished' WHERE id = {id}");
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => Planner.FeaturePlanAsync(Client, id, p, session, null));

        Assert.Equal(SmErrorCodes.FeatureNeedsPosition, ex.Code);
        Assert.Contains("create-feature", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FeaturePlan_StepsResolved()
    {
        var feature = Feature(UniqueName(), "DocumentListLayout", "Grid.PageSize",
            steps: [new SmFeatureStep { Text = "Run for {{processname}}", Sql = "SELECT '{{processname}}'" }]);
        var id = await SaveAsync(feature);
        var (p, session) = await NewProcessAsync();

        var result = await Planner.FeaturePlanAsync(Client, id, p, session, null);

        var step = Assert.Single(result.Steps);
        Assert.Equal($"SELECT '{p}'", step.Sql);
        Assert.Equal($"Run for {p}", step.Text);
        Assert.Equal(p, result.Values["processname"]);
    }
}
