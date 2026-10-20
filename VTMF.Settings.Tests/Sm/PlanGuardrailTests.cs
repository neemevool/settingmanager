using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class PlanGuardrailTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task ServerSettingRow_ThrowsServerSetting()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.Secret", "new")), "secret", session.Id));

        Assert.Equal(SmErrorCodes.ServerSetting, ex.Code);
        Assert.Contains("Grid.Secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Sm.GetPlansAsync(Client, p));
    }

    [Fact]
    public async Task ServerSettingType_ThrowsServerSetting()
    {
        var (p, session) = await NewProcessAsync();
        var row = Row("Grid.NewSecret", "x", settingType: "ServerSetting");

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, row), "secret type", session.Id));

        Assert.Equal(SmErrorCodes.ServerSetting, ex.Code);
    }

    [Fact]
    public async Task Replace_KeepsServerSettingRows()
    {
        var (p, session) = await NewProcessAsync();
        var current = await fixture.Sm.GetSettingsAsync(Client, p, Table);
        var desired = new SmDesiredState
        {
            Process = p,
            Mode = "replace",
            Scope = [new SmScopeEntry { Table = Table, Layer = "base" }],
            Rows = current
                .Where(r => r.Name != "Grid.Secret")
                .Select(r => new SmDesiredRow
                {
                    Table = Table, Layer = "base", Name = r.Name, Value = r.Value, SettingType = r.SettingType,
                    ListValue = r.ListValue, OrderNr = r.OrderNr,
                })
                .ToList(),
        };

        var result = await fixture.Sm.CreatePlanAsync(Client, desired, "replace", session.Id);

        Assert.Equal("NoChanges", result.Status);
        Assert.DoesNotContain(result.Items, i => i.Name == "Grid.Secret");
    }

    [Fact]
    public async Task Groups_SharingName_AreDistinct()
    {
        var (p, session) = await NewProcessAsync();

        var lot = await fixture.Sm.CreatePlanAsync(
            Client, Merge(p, Row("Column", "LOT", orderNr: 35, settingType: "SettingGroup")), "lot", session.Id, dryRun: true);

        var insert = Assert.Single(lot.Items);
        Assert.Equal("Insert", insert.Action);
        Assert.Equal("LOT", insert.Value);
        Assert.DoesNotContain(lot.Items, i => i.Action is "Delete" or "Update");

        await PlanSandbox.ExecAsync(fixture, Client, $"""
            INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Detail', N'A', N'SettingGroup', NULL, N'ALL', 70),
             (N'Detail', N'B', N'SettingGroup', NULL, N'ALL', 80);
            """);

        var detail = await fixture.Sm.CreatePlanAsync(
            Client, Merge(p, Row("Detail", "A", orderNr: 99, settingType: "SettingGroup")), "detail", session.Id, dryRun: true);

        var update = Assert.Single(detail.Items);
        Assert.Equal("Update", update.Action);
        Assert.Equal("Detail", update.Name);
        Assert.Equal("A", update.Value);
        Assert.Equal("A", update.OldValue);
        Assert.Equal(99, update.OrderNr);
        Assert.Equal(70, update.OldOrderNr);
    }

    [Fact]
    public async Task NameKeyedGroup_ValueChangeIsUpdate()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Detail.Lines', N'X', N'SettingGroup', NULL, N'ALL', 70);");

        var result = await fixture.Sm.CreatePlanAsync(
            Client, Merge(p, Row("Detail.Lines", "Y", settingType: "SettingGroup")), "lines", session.Id, dryRun: true);

        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Y", item.Value);
        Assert.Equal("X", item.OldValue);
    }

    [Fact]
    public async Task DuplicateSetting_IsRefused()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.PageSize', N'30', N'Setting', NULL, N'ALL', 55);");

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "dup", session.Id));

        Assert.Equal(SmErrorCodes.DuplicateRow, ex.Code);
        Assert.Contains(p + Table, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Grid.PageSize", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ALL", ex.Message, StringComparison.Ordinal);

        var other = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Column.DATE.Caption", "Date")), "untouched dup", session.Id);

        Assert.Equal("Created", other.Status);
        Assert.Equal("Update", Assert.Single(other.Items).Action);
    }

    [Fact]
    public async Task MissingTable_ThrowsTableMissing()
    {
        var (p, session) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50", table: "DocumentNewLayout")), "missing", session.Id));

        Assert.Equal(SmErrorCodes.TableMissing, ex.Code);
        Assert.Contains(p + "DocumentNewLayout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTable_WithCreateTable_PlansCreateTable()
    {
        var (p, session) = await NewProcessAsync();
        var options = new SmPlanOptions { CreateTable = true };

        var result = await fixture.Sm.CreatePlanAsync(
            Client, Merge(p, Row("Grid.PageSize", "50", table: "DocumentNewLayout")), "create", session.Id, options);

        Assert.Equal("Created", result.Status);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("CreateTable", result.Items[0].Action);
        Assert.Equal(p + "DocumentNewLayout", result.Items[0].PhysicalTable);
        Assert.Equal(1, result.Items[0].Seq);
        Assert.Equal("Insert", result.Items[1].Action);
        Assert.Equal(p + "DocumentNewLayout", result.Items[1].PhysicalTable);
        Assert.Equal("Grid.PageSize", result.Items[1].Name);
        Assert.Contains(result.Warnings, w => w.Code == "CREATE_TABLE" && w.Message.Contains(p + "DocumentNewLayout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AliasProcess_WithoutOwnTable_ThrowsAliasTarget()
    {
        var (p, _) = await NewProcessAsync();
        var (variant, session) = await NewVariantAsync(p);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.CreatePlanAsync(Client, Merge(variant, Row("Grid.PageSize", "50")), "alias", session.Id));

        Assert.Equal(SmErrorCodes.AliasTarget, ex.Code);
        Assert.Contains(p, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AliasProcess_TargetAlias_WritesAliasTableAndWarns()
    {
        var (p, _) = await NewProcessAsync();
        var (variant, session) = await NewVariantAsync(p);
        var options = new SmPlanOptions { TargetAlias = true };

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(variant, Row("Grid.PageSize", "50")), "alias", session.Id, options);

        var item = Assert.Single(result.Items);
        Assert.Equal("Update", item.Action);
        Assert.Equal(p + Table, item.PhysicalTable);
        Assert.Equal("20", item.OldValue);
        var warning = Assert.Single(result.Warnings, w => w.Code == "ALIAS_TARGET");
        Assert.Contains(p + Table, warning.Message, StringComparison.Ordinal);
        Assert.Contains(variant, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverrideProc_Warns()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"CREATE PROCEDURE dbo.Get{p}{Table}Info AS SELECT 1;");

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "override", session.Id, dryRun: true);

        var warning = Assert.Single(result.Warnings, w => w.Code == "OVERRIDE_PROC");
        Assert.Equal("Warning", warning.Severity);
        Assert.Contains($"Get{p}{Table}Info", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InactiveOverrideProc_DoesNotWarn()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"CREATE PROCEDURE dbo.[Get{p}{Table}Info-] AS SELECT 1;");

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "override", session.Id, dryRun: true);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "OVERRIDE_PROC");
    }

    [Fact]
    public async Task LegacyEnvRow_Warns()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.PageSize', N'99', N'Setting', NULL, N'wterminal2', 50);");

        var result = await fixture.Sm.CreatePlanAsync(Client, Merge(p, Row("Grid.PageSize", "50")), "legacy", session.Id, dryRun: true);

        Assert.Equal("Update", Assert.Single(result.Items).Action);
        var warning = Assert.Single(result.Warnings, w => w.Code == "LEGACY_ENV");
        Assert.Equal("Warning", warning.Severity);
        Assert.Contains(p + Table, warning.Message, StringComparison.Ordinal);
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private async Task<(string Process, SmSession Session)> NewVariantAsync(string parent)
    {
        var variant = await PlanSandbox.CreateProcessAsync(fixture, Client, settingProcedures: parent);
        await PlanSandbox.ExecAsync(fixture, Client, $"DROP TABLE dbo.{variant}{Table};");
        var session = await fixture.Sm.StartSessionAsync(Client, "test", variant);
        return (variant, session);
    }

    private static SmDesiredRow Row(string name, string? value, int? orderNr = null, string settingType = "Setting", string table = Table) =>
        new() { Table = table, Name = name, Value = value, OrderNr = orderNr, SettingType = settingType };

    private static SmDesiredState Merge(string process, params SmDesiredRow[] rows) =>
        new() { Process = process, Rows = rows };
}
