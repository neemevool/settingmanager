using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class LegacyEnvMigrationTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";
    private const string Env = "wterminal2";
    private const string Template = "NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL";

    [Fact]
    public async Task Migration_MovesRowToNewOverlay()
    {
        var (p, session) = await NewProcessAsync();
        await InsertAsync(p + Table, "Grid.PageSize", "30", Env, 55);
        await InsertAsync(p + Table, "Grid.Secret", "hidden", Env, 65, "ServerSetting");

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("Created", plan.Status);
        Assert.Equal(["CreateTable", "Insert", "Delete"], plan.Items.Select(i => i.Action).ToArray());
        Assert.Equal([1, 2, 3], plan.Items.Select(i => i.Seq).ToArray());
        var create = plan.Items[0];
        Assert.Equal(p + Table + Env, create.PhysicalTable);
        Assert.Equal(Table, create.Table);
        Assert.Equal("env:" + Env, create.Layer);
        Assert.Null(create.Name);
        var insert = plan.Items[1];
        Assert.Equal(p + Table + Env, insert.PhysicalTable);
        Assert.Equal("env:" + Env, insert.Layer);
        Assert.Equal(Env, insert.WorkerCode);
        Assert.Equal("Grid.PageSize", insert.Name);
        Assert.Equal("30", insert.Value);
        Assert.Equal("Setting", insert.SettingType);
        Assert.Equal(55, insert.OrderNr);
        Assert.Null(insert.OldValue);
        var delete = plan.Items[2];
        Assert.Equal(p + Table, delete.PhysicalTable);
        Assert.Equal("base", delete.Layer);
        Assert.Equal(Env, delete.WorkerCode);
        Assert.Equal("Grid.PageSize", delete.Name);
        Assert.Null(delete.Value);
        Assert.Equal("30", delete.OldValue);
        Assert.Equal("Setting", delete.OldSettingType);
        Assert.Equal(55, delete.OldOrderNr);
        Assert.Contains(plan.Warnings, w => w.Code == "CREATE_TABLE");
        var legacyEnv = Assert.Single(plan.Warnings, w => w.Code == "LEGACY_ENV");
        Assert.Equal("Warning", legacyEnv.Severity);
        Assert.Contains("Grid.PageSize", legacyEnv.Message, StringComparison.Ordinal);
        Assert.Contains(p + Table + Env, legacyEnv.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Warnings, w => w.Message.Contains("hidden", StringComparison.Ordinal));
        var before = await fixture.Sm.GetEffectiveSettingsAsync(Client, p, Table, environment: Env);
        Assert.Equal("20", Assert.Single(before.Rows, r => r.Name == "Grid.PageSize").Value);

        var stored = (await fixture.Sm.GetPlanAsync(Client, plan.PlanId!.Value)).Plan;
        Assert.Equal("migration", stored.Source);
        Assert.Equal("merge", stored.Mode);
        Assert.Equal(session.Id, stored.SessionId);
        Assert.Equal(p, stored.Process);

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId.Value, plan.Checksum!);

        Assert.Equal("Applied", result.Status);
        var effective = await fixture.Sm.GetEffectiveSettingsAsync(Client, p, Table, environment: Env);
        Assert.Equal("30", Assert.Single(effective.Rows, r => r.Name == "Grid.PageSize").Value);
        var legacy = await PlanSandbox.RowsAsync(fixture, Client, p + Table, Env);
        Assert.Equal("Grid.Secret", Assert.Single(legacy).Name);
        var overlay = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table + Env, Env));
        Assert.Equal(55, overlay.OrderNr);
    }

    [Fact]
    public async Task Migration_SameValueInOverlay_OnlyDeletesLegacy()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"CREATE TABLE dbo.{p}{Table}{Env} ({Template});");
        await InsertAsync(p + Table + Env, "Grid.PageSize", "30", Env, 50);
        await InsertAsync(p + Table, "Grid.PageSize", "30", Env, 55);

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("Created", plan.Status);
        var item = Assert.Single(plan.Items);
        Assert.Equal("Delete", item.Action);
        Assert.Equal(p + Table, item.PhysicalTable);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.DoesNotContain(plan.Warnings, w => w.Code == "CREATE_TABLE");
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!)).Status);
        Assert.Empty(await PlanSandbox.RowsAsync(fixture, Client, p + Table, Env));
        var overlay = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table + Env, Env));
        Assert.Equal(50, overlay.OrderNr);
    }

    [Fact]
    public async Task Migration_ConflictingOverlay_WarnsAndKeepsLegacy()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"CREATE TABLE dbo.{p}{Table}{Env} ({Template});");
        await InsertAsync(p + Table + Env, "Grid.PageSize", "40", Env, 50);
        await InsertAsync(p + Table, "Grid.PageSize", "30", Env, 55);
        await InsertAsync(p + Table, "Grid.ShowFilterRow", "true", Env, 56);

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("Created", plan.Status);
        Assert.Equal(["Insert", "Delete"], plan.Items.Select(i => i.Action).ToArray());
        Assert.All(plan.Items, i => Assert.Equal("Grid.ShowFilterRow", i.Name));
        var warning = Assert.Single(plan.Warnings, w => w.Code == "LEGACY_CONFLICT");
        Assert.Contains(p + Table, warning.Message, StringComparison.Ordinal);
        Assert.Contains(Env, warning.Message, StringComparison.Ordinal);
        Assert.Contains("Grid.PageSize", warning.Message, StringComparison.Ordinal);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!)).Status);
        var legacy = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table, Env));
        Assert.Equal("Grid.PageSize", legacy.Name);
        Assert.Equal("30", legacy.Value);
        var overlay = await PlanSandbox.RowsAsync(fixture, Client, p + Table + Env, Env);
        Assert.Equal("40", Assert.Single(overlay, r => r.Name == "Grid.PageSize").Value);
    }

    [Theory]
    [InlineData("30", "a|b", "30", null)]
    [InlineData("30", null, "30", "a|b")]
    [InlineData("True", null, "true", null)]
    public async Task Migration_OverlayDiffersOnlyInListValueOrCase_IsConflict(
        string overlayValue, string? overlayList, string legacyValue, string? legacyList)
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"CREATE TABLE dbo.{p}{Table}{Env} ({Template});");
        await InsertAsync(p + Table + Env, "Grid.PageSize", overlayValue, Env, 50, listValue: overlayList);
        await InsertAsync(p + Table, "Grid.PageSize", legacyValue, Env, 55, listValue: legacyList);

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("NoChanges", plan.Status);
        Assert.Empty(plan.Items);
        var warning = Assert.Single(plan.Warnings, w => w.Code == "LEGACY_CONFLICT");
        Assert.Contains("Grid.PageSize", warning.Message, StringComparison.Ordinal);
        Assert.Equal(legacyValue, Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table, Env)).Value);
    }

    [Fact]
    public async Task Migration_TwoCharacterEnvironment_WarnsAndSkips()
    {
        var (p, session) = await NewProcessAsync();
        await InsertAsync(p + Table, "Grid.PageSize", "30", "w2", 55);

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("NoChanges", plan.Status);
        Assert.Empty(plan.Items);
        var warning = Assert.Single(plan.Warnings, w => w.Code == "LEGACY_CONFLICT");
        Assert.Contains("'w2'", warning.Message, StringComparison.Ordinal);
        Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, p + Table, "w2"));
    }

    [Fact]
    public async Task Migration_UnsafeEnvironment_WarnsAndSkips()
    {
        var (p, session) = await NewProcessAsync();
        await InsertAsync(p + Table, "Grid.PageSize", "30", "bad-env", 55);

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("NoChanges", plan.Status);
        Assert.Empty(plan.Items);
        var warning = Assert.Single(plan.Warnings, w => w.Code == "LEGACY_CONFLICT");
        Assert.Contains("bad-env", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_NoLegacyRows_ReturnsNoChanges()
    {
        var (p, session) = await NewProcessAsync();

        var plan = await fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id);

        Assert.Equal("NoChanges", plan.Status);
        Assert.Null(plan.PlanId);
        Assert.Null(plan.Checksum);
        Assert.Empty(plan.Items);
        Assert.Equal(0, await fixture.ScalarOnAsync<int>(Client, $"SELECT COUNT(*) FROM sm.[plan] WHERE sessionid = {session.Id}"));
    }

    [Fact]
    public async Task Migration_DuplicateLegacyRows_ThrowsDuplicateRow()
    {
        var (p, session) = await NewProcessAsync();
        await InsertAsync(p + Table, "Grid.PageSize", "30", Env, 55);
        await InsertAsync(p + Table, "Grid.PageSize", "31", Env, 56);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id));

        Assert.Equal(SmErrorCodes.DuplicateRow, ex.Code);
    }

    [Fact]
    public async Task Migration_ClosedSession_ThrowsSessionClosed()
    {
        var (p, session) = await NewProcessAsync();
        await fixture.Sm.CloseSessionAsync(Client, session.Id);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, session.Id));

        Assert.Equal(SmErrorCodes.SessionClosed, ex.Code);
    }

    [Fact]
    public async Task Migration_UnknownSession_ThrowsSessionNotFound()
    {
        var (p, _) = await NewProcessAsync();

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateLegacyEnvMigrationPlanAsync(Client, p, int.MaxValue));

        Assert.Equal(SmErrorCodes.SessionNotFound, ex.Code);
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private Task InsertAsync(string table, string name, string value, string workercode, int ordernr, string settingtype = "Setting", string? listValue = null) =>
        PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.[{table}] (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'{name}', N'{value}', N'{settingtype}', {(listValue is null ? "NULL" : $"N'{listValue}'")}, N'{workercode}', {ordernr});");
}
