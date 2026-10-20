using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class RollbackPlanTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";
    private const string NewTable = "DocumentNewLayout";

    [Fact]
    public async Task Rollback_RestoresValues_AndMarksOriginalRolledBack()
    {
        var (p, session) = await NewProcessAsync();
        var original = await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        Assert.Equal("Created", rollback.Status);
        Assert.NotNull(rollback.Checksum);
        var item = Assert.Single(rollback.Items);
        Assert.Equal(1, item.Seq);
        Assert.Equal("Update", item.Action);
        Assert.Equal("Grid.PageSize", item.Name);
        Assert.Equal("20", item.Value);
        Assert.Equal("50", item.OldValue);
        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum);
        Assert.Equal("Applied", result.Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
        var stored = (await fixture.Sm.GetPlanAsync(Client, rollback.PlanId.Value)).Plan;
        Assert.Equal(original, stored.RollbackOf);
        Assert.Equal(session.Id, stored.SessionId);
        Assert.Equal(p, stored.Process);
        Assert.Equal("merge", stored.Mode);
        Assert.Equal($"rollback:{original}", stored.Source);
        Assert.Equal($"Rollback of plan {original}: apply", stored.Description);
        Assert.Equal("Applied", stored.Status);
    }

    [Fact]
    public async Task Rollback_NotApplied_ThrowsPlanStatus()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await fixture.Sm.CreatePlanAsync(Client, Desired(p, Row("Grid.PageSize", "50")), "apply", session.Id);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateRollbackPlanAsync(Client, plan.PlanId!.Value));

        Assert.Equal(SmErrorCodes.PlanStatus, ex.Code);
    }

    [Fact]
    public async Task Rollback_UnknownPlan_ThrowsPlanNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateRollbackPlanAsync(Client, int.MaxValue));

        Assert.Equal(SmErrorCodes.PlanNotFound, ex.Code);
    }

    [Fact]
    public async Task Rollback_DuplicateOfTouchedRow_ThrowsDuplicateRow_AndStoresNothing()
    {
        var (p, session) = await NewProcessAsync();
        var original = await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.PageSize', N'70', N'Setting', NULL, N'ALL', 55);");

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CreateRollbackPlanAsync(Client, original));

        Assert.Equal(SmErrorCodes.DuplicateRow, ex.Code);
        Assert.Contains("Grid.PageSize", ex.Message, StringComparison.Ordinal);
        Assert.Contains(p + Table, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await fixture.ScalarOnAsync<int>(Client, $"SELECT COUNT(*) FROM sm.[plan] WHERE rollbackof = {original}"));
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
    }

    [Fact]
    public async Task Rollback_ClosedSession_IsAllowed()
    {
        var (p, session) = await NewProcessAsync();
        var original = await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"));
        await fixture.Sm.CloseSessionAsync(Client, session.Id);

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        Assert.Equal("Created", rollback.Status);
        Assert.Equal(session.Id, (await fixture.Sm.GetPlanAsync(Client, rollback.PlanId!.Value)).Plan.SessionId);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId.Value, rollback.Checksum!)).Status);
    }

    [Fact]
    public async Task Rollback_AfterLaterChange_IsStale()
    {
        var (p, session) = await NewProcessAsync();
        var original = await ApplyAsync(p, session, null, Row("Grid.PageSize", "50"), Row("Column.DATE.Caption", "Date"));
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET VALUE = N'60' WHERE NAME = N'Grid.PageSize';");
        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Grid.PageSize", conflict.Name);
        Assert.Equal("changed", conflict.Reason);
        Assert.Equal("50", conflict.ExpectedValue);
        Assert.Equal("60", conflict.CurrentValue);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("Date", Assert.Single(rows, r => r.Name == "Column.DATE.Caption").Value);
        Assert.Equal("60", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
    }

    [Fact]
    public async Task Rollback_Renumber_RestoresOrder()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 11 WHERE NAME = N'Column.DOCNR.Caption';");
        var before = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        var original = await ApplyAsync(p, session, null, Row("Column.DOCNR.Width", "100", after: "Column=DOCNR"));

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        var secret = Assert.Single(rollback.Items, i => i.Name == "Grid.Secret");
        Assert.Equal("Update", secret.Action);
        Assert.Null(secret.Value);
        Assert.Null(secret.OldValue);
        Assert.Equal(60, secret.OrderNr);
        Assert.Equal(70, secret.OldOrderNr);
        Assert.Equal("Applied", (await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!)).Status);
        var after = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal(
            before.Select(r => (r.Name, r.Value, r.SettingType, r.OrderNr)).ToArray(),
            after.Select(r => (r.Name, r.Value, r.SettingType, r.OrderNr)).ToArray());
    }

    [Fact]
    public async Task Rollback_CreateTable_BacksUpAndDrops()
    {
        var (p, session) = await NewProcessAsync();
        var (_, backup, _) = await CreateAndRollBackTableAsync(p, session);

        var rows = await PlanSandbox.RowsAsync(fixture, Client, backup);
        Assert.Equal(["Grid.PageSize", "Grid.ShowFilterRow"], rows.Select(r => r.Name).Order(StringComparer.Ordinal).ToArray());
        var marker = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, backup, "SYSTEM"));
        Assert.Equal("Backup.Description", marker.Name);
        Assert.Equal("BackupDescription", marker.SettingType);
        Assert.Equal(100000, marker.OrderNr);
        Assert.StartsWith("Rollback of plan ", marker.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rollback_CreateTable_WithForeignRow_IsStale()
    {
        var (p, session) = await NewProcessAsync();
        var table = p + NewTable;
        var original = await ApplyAsync(p, session, new SmPlanOptions { CreateTable = true },
            Row("Grid.PageSize", "50", table: NewTable), Row("Grid.ShowFilterRow", "true", table: NewTable));
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.Other', N'1', N'Setting', NULL, N'ALL', 99);");
        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("table_not_empty", conflict.Reason);
        Assert.Equal("DropTable", conflict.Action);
        Assert.NotNull(await fixture.ScalarOnAsync<int?>(Client, $"SELECT OBJECT_ID(N'dbo.{table}', N'U')"));
        Assert.Equal(3, (await PlanSandbox.RowsAsync(fixture, Client, table)).Count);
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
    }

    [Fact]
    public async Task RollbackOfRollback_RecreatesTable()
    {
        var (p, session) = await NewProcessAsync();
        var (_, _, rollbackId) = await CreateAndRollBackTableAsync(p, session);

        var redo = await fixture.Sm.CreateRollbackPlanAsync(Client, rollbackId);

        Assert.Equal(["CreateTable", "Insert", "Insert"], redo.Items.Select(i => i.Action).ToArray());
        Assert.Equal([1, 2, 3], redo.Items.Select(i => i.Seq).ToArray());
        var result = await fixture.Sm.ApplyPlanAsync(Client, redo.PlanId!.Value, redo.Checksum!);
        Assert.Equal("Applied", result.Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + NewTable);
        Assert.Equal(
            [("Grid.PageSize", "50"), ("Grid.ShowFilterRow", "true")],
            rows.Select(r => (r.Name, r.Value)).OrderBy(r => r.Name, StringComparer.Ordinal).ToArray());
        Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, rollbackId)).Plan.Status);
    }

    /// <summary>Applies a createTable plan with 2 rows, then creates and applies its rollback. Returns (original, backup table, rollback plan id).</summary>
    private async Task<(int Original, string Backup, int RollbackId)> CreateAndRollBackTableAsync(string p, SmSession session)
    {
        var table = p + NewTable;
        var original = await ApplyAsync(p, session, new SmPlanOptions { CreateTable = true },
            Row("Grid.PageSize", "50", table: NewTable), Row("Grid.ShowFilterRow", "true", table: NewTable));

        var rollback = await fixture.Sm.CreateRollbackPlanAsync(Client, original);

        Assert.Equal(["BackupTable", "Delete", "Delete", "DropTable"], rollback.Items.Select(i => i.Action).ToArray());
        Assert.Equal([1, 2, 3, 4], rollback.Items.Select(i => i.Seq).ToArray());
        var result = await fixture.Sm.ApplyPlanAsync(Client, rollback.PlanId!.Value, rollback.Checksum!);
        Assert.Equal("Applied", result.Status);
        Assert.Null(await fixture.ScalarOnAsync<int?>(Client, $"SELECT OBJECT_ID(N'dbo.{table}', N'U')"));
        var backup = (await fixture.Sm.GetPlanAsync(Client, rollback.PlanId.Value)).Items[0].BackupTable;
        Assert.NotNull(backup);
        Assert.StartsWith("bck_" + p + NewTable + "_", backup, StringComparison.Ordinal);
        Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, original)).Plan.Status);
        return (original, backup, rollback.PlanId.Value);
    }

    private async Task<int> ApplyAsync(string process, SmSession session, SmPlanOptions? options, params SmDesiredRow[] rows)
    {
        var plan = await fixture.Sm.CreatePlanAsync(Client, Desired(process, rows), "apply", session.Id, options);
        Assert.Equal("Created", plan.Status);
        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);
        Assert.Equal("Applied", result.Status);
        return plan.PlanId.Value;
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredState Desired(string process, params SmDesiredRow[] rows) => new() { Process = process, Rows = rows };

    private static SmDesiredRow Row(string name, string value, string? after = null, string table = Table) =>
        new() { Table = table, Name = name, Value = value, After = after };
}
