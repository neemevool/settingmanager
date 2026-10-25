using VTMF.Settings.Core.Sm;
using VTMF.Settings.Core.Sm.Features;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class FeatureStoreTests(SqlServerFixture fixture)
{
    private static string UniqueName() => "t-" + Guid.NewGuid().ToString("N")[..8];

    private static SmFeature Sample(string name, int rows = 2) => new()
    {
        Name = name,
        Description = "Allows adding a barcode",
        BuiltOnProcess = "sparelocateitems",
        Rows = Enumerable.Range(1, rows).Select(i => new SmFeatureRow
        {
            Table = "ProcessSettings",
            Name = $"{name}.Row{i}",
            Value = i == 1 ? "Kogus õäöü" : "{{p}}",
            Note = i == 1 ? "first" : null,
        }).ToList(),
        Params =
        [
            new SmFeatureParam
            {
                Name = "p", Description = "pick", Type = "List", Default = "B",
                Values = [new SmFeatureParamValue { Value = "B", Description = "bee" }, new SmFeatureParamValue { Value = "A", Description = "ay" }],
            },
            new SmFeatureParam { Name = "q", Description = "text", Type = "Text" },
        ],
        Steps = [new SmFeatureStep { Text = "one", Sql = "SELECT 1" }, new SmFeatureStep { Text = "two" }],
    };

    private async Task<(int Id, long Revision)> SaveNewAsync(string name, int rows = 2)
    {
        var saved = await fixture.FeatureStore.SaveFeatureAsync(Sample(name, rows), null);
        return (saved.Id, saved.Revision);
    }

    [Fact]
    public async Task Save_New_IsDraftAndRoundTrips()
    {
        var name = UniqueName();
        var (id, revision) = await SaveNewAsync(name);

        var feature = await fixture.FeatureStore.GetFeatureAsync(id, null);

        Assert.Equal("Draft", feature.Status);
        Assert.Equal(name, feature.Name);
        Assert.Equal(revision, feature.Revision);
        Assert.Equal("sparelocateitems", feature.BuiltOnProcess);
        Assert.False(string.IsNullOrEmpty(feature.CreatedBy));
        Assert.Equal([$"{name}.Row1", $"{name}.Row2"], feature.Rows.Select(r => r.Name).ToArray());
        Assert.Equal("Kogus õäöü", feature.Rows[0].Value);
        Assert.Equal("base", feature.Rows[0].Layer);
        Assert.Equal("set", feature.Rows[0].Op);
        Assert.Equal(["p", "q"], feature.Params.Select(p => p.Name).ToArray());
        Assert.Equal("B", feature.Params[0].Default);
        Assert.Equal(["B", "A"], feature.Params[0].Values.Select(v => v.Value).ToArray());
        Assert.Empty(feature.Params[1].Values);
        Assert.Equal(["one", "two"], feature.Steps.Select(s => s.Text).ToArray());
        Assert.Equal("SELECT 1", feature.Steps[0].Sql);
        Assert.Null(feature.Steps[1].Sql);

        var byName = await fixture.FeatureStore.GetFeatureAsync(null, name.ToUpperInvariant());
        Assert.Equal(id, byName.Id);
    }

    [Fact]
    public async Task Save_Replace_ReplacesChildren()
    {
        var name = UniqueName();
        var (id, revision) = await SaveNewAsync(name);

        var replaced = await fixture.FeatureStore.SaveFeatureAsync(
            new SmFeature
            {
                Id = id, Name = name, Rows = Sample(name, 1).Rows, Steps = [],
            }, revision);

        var feature = await fixture.FeatureStore.GetFeatureAsync(id, null);
        Assert.Single(feature.Rows);
        Assert.Empty(feature.Params);
        Assert.Empty(feature.Steps);
        Assert.NotEqual(revision, replaced.Revision);
        Assert.Equal("Draft", feature.Status);
    }

    [Fact]
    public async Task Save_StaleRevision_IsFeatureChanged()
    {
        var name = UniqueName();
        var (id, revision) = await SaveNewAsync(name);
        await fixture.FeatureStore.SaveFeatureAsync(new SmFeature { Id = id, Name = name }, revision);

        var stale = await Assert.ThrowsAsync<SmException>(() =>
            fixture.FeatureStore.SaveFeatureAsync(new SmFeature { Id = id, Name = name }, revision));
        var missing = await Assert.ThrowsAsync<SmException>(() =>
            fixture.FeatureStore.SaveFeatureAsync(new SmFeature { Id = id, Name = name }, null));

        Assert.Equal(SmErrorCodes.FeatureChanged, stale.Code);
        Assert.Equal(SmErrorCodes.FeatureChanged, missing.Code);
    }

    [Fact]
    public async Task Save_NameTakenCaseInsensitive_IsRefused()
    {
        var name = UniqueName();
        await SaveNewAsync(name);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.FeatureStore.SaveFeatureAsync(new SmFeature { Name = name.ToUpperInvariant() }, null));

        Assert.Equal(SmErrorCodes.FeatureNameTaken, ex.Code);
    }

    [Fact]
    public async Task Save_TooLongValue_IsFeatureInvalid()
    {
        var name = UniqueName();
        var feature = new SmFeature
        {
            Name = name,
            Rows = [new SmFeatureRow { Table = "ProcessSettings", Name = "A", Value = new string('x', 401) }],
        };

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.FeatureStore.SaveFeatureAsync(feature, null));

        Assert.Equal(SmErrorCodes.FeatureInvalid, ex.Code);
        Assert.Contains("Row 1 value is longer than 400", ex.Message);
        Assert.Empty(await fixture.FeatureStore.FindFeaturesAsync(name, null));
    }

    [Fact]
    public async Task Find_DefaultsToFinished_DraftsOnRequest()
    {
        var name = UniqueName();
        var (id, _) = await SaveNewAsync(name);

        var byDefault = await fixture.FeatureStore.FindFeaturesAsync(name);
        var drafts = await fixture.FeatureStore.FindFeaturesAsync(name, "Draft");
        var byRowName = await fixture.FeatureStore.FindFeaturesAsync($"{name}.Row2", null);

        Assert.Empty(byDefault);
        var found = Assert.Single(drafts);
        Assert.Equal(id, found.Id);
        Assert.Equal("ProcessSettings", found.TableKinds);
        Assert.Equal(2, found.ParamCount);
        Assert.Equal(id, Assert.Single(byRowName).Id);
    }

    [Fact]
    public async Task SetStatus_FinishAndReopen()
    {
        var (id, revision) = await SaveNewAsync(UniqueName());

        var finished = await fixture.FeatureStore.SetFeatureStatusAsync(id, "Finished", revision);
        Assert.Equal("Finished", (await fixture.FeatureStore.GetFeatureAsync(id, null)).Status);
        Assert.NotEqual(revision, finished.Revision);

        var reopened = await fixture.FeatureStore.SetFeatureStatusAsync(id, "Draft", finished.Revision);
        Assert.Equal("Draft", (await fixture.FeatureStore.GetFeatureAsync(id, null)).Status);
        Assert.NotEqual(finished.Revision, reopened.Revision);
    }

    [Fact]
    public async Task Delete_Finished_IsRefused()
    {
        var (id, revision) = await SaveNewAsync(UniqueName());
        var finished = await fixture.FeatureStore.SetFeatureStatusAsync(id, "Finished", revision);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.FeatureStore.DeleteFeatureAsync(id, finished.Revision));

        Assert.Equal(SmErrorCodes.FeatureStatus, ex.Code);
    }

    [Fact]
    public async Task SetStatus_FinishInvalid_IsFeatureInvalid()
    {
        var name = UniqueName();
        var saved = await fixture.FeatureStore.SaveFeatureAsync(
            new SmFeature { Name = name, Rows = [new SmFeatureRow { Table = "ProcessSettings", Name = "A", Value = "{{undeclared}}" }] }, null);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.FeatureStore.SetFeatureStatusAsync(saved.Id, "Finished", saved.Revision));

        Assert.Equal(SmErrorCodes.FeatureInvalid, ex.Code);
        Assert.Contains("undeclared", ex.Message);
        Assert.Equal("Draft", (await fixture.FeatureStore.GetFeatureAsync(saved.Id, null)).Status);
    }

    [Fact]
    public async Task Save_FinishedMadeInvalid_IsRefused()
    {
        var (id, revision) = await SaveNewAsync(UniqueName());
        var finished = await fixture.FeatureStore.SetFeatureStatusAsync(id, "Finished", revision);
        var current = await fixture.FeatureStore.GetFeatureAsync(id, null);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.FeatureStore.SaveFeatureAsync(
            new SmFeature { Id = id, Name = current.Name, Description = "", Rows = current.Rows, Params = current.Params, Steps = current.Steps },
            finished.Revision));

        Assert.Equal(SmErrorCodes.FeatureInvalid, ex.Code);
    }

    [Fact]
    public async Task SaveFeature_PositionParam_RoundTrips()
    {
        var name = UniqueName();
        var saved = await fixture.FeatureStore.SaveFeatureAsync(new SmFeature
        {
            Name = name,
            Description = "Adds a barcode column",
            Rows =
            [
                new SmFeatureRow { Table = "DocumentListLayout", Name = "Column", Value = "BARCODE", SettingType = "SettingGroup", After = "{{pos}}" },
                new SmFeatureRow { Table = "DocumentListLayout", Name = "Column.BARCODE.Caption", Value = "Barcode" },
            ],
            Params = [new SmFeatureParam { Name = "pos", Description = "After which column should the barcode column be shown? (or first)", Type = "Position" }],
        }, null);
        await fixture.FeatureStore.SetFeatureStatusAsync(saved.Id, "Finished", saved.Revision);

        var feature = await fixture.FeatureStore.GetFeatureAsync(saved.Id, null);

        Assert.Equal("Finished", feature.Status);
        var param = Assert.Single(feature.Params);
        Assert.Equal("Position", param.Type);
        Assert.Null(param.Default);
        Assert.Equal("{{pos}}", feature.Rows[0].After);
    }

    [Fact]
    public async Task Delete_Draft_Removes()
    {
        var (id, revision) = await SaveNewAsync(UniqueName());

        await fixture.FeatureStore.DeleteFeatureAsync(id, revision);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.FeatureStore.GetFeatureAsync(id, null));
        Assert.Equal(SmErrorCodes.FeatureNotFound, ex.Code);
    }
}
