using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class ApplyPlanTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;
    private const string Table = "DocumentListLayout";

    [Fact]
    public async Task Apply_UpdatesRowsAndMarksApplied()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Grid.PageSize", "50"));

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Applied", result.Status);
        Assert.Equal(plan.PlanId.Value, result.PlanId);
        Assert.Empty(result.Conflicts);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("50", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Applied", details.Plan.Status);
        Assert.False(string.IsNullOrEmpty(details.Plan.AppliedBy));
        Assert.NotNull(details.Plan.AppliedAt);
    }

    [Fact]
    public async Task Apply_WrongChecksum_ThrowsChecksumMismatch_AndKeepsCreated()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Grid.PageSize", "50"));

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, new string('0', 64)));

        Assert.Equal(SmErrorCodes.ChecksumMismatch, ex.Code);
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId!.Value);
        Assert.Equal("Created", details.Plan.Status);
        Assert.Null(details.Plan.AppliedAt);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
    }

    [Fact]
    public async Task Apply_Twice_ThrowsPlanStatus()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Grid.PageSize", "50"));
        await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!));

        Assert.Equal(SmErrorCodes.PlanStatus, ex.Code);
    }

    [Fact]
    public async Task Apply_UnknownPlan_ThrowsPlanNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() =>
            fixture.Sm.ApplyPlanAsync(Client, int.MaxValue, new string('0', 64)));

        Assert.Equal(SmErrorCodes.PlanNotFound, ex.Code);
    }

    [Fact]
    public async Task Apply_RowChangedAfterPreview_IsStaleAndUnchanged()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Grid.PageSize", "50"), Row("Column.DATE.Caption", "Date"));
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET VALUE = N'30' WHERE NAME = N'Grid.PageSize';");

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("changed", conflict.Reason);
        Assert.Equal("Grid.PageSize", conflict.Name);
        Assert.Equal("20", conflict.ExpectedValue);
        Assert.Equal("30", conflict.CurrentValue);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("Kuupäev", Assert.Single(rows, r => r.Name == "Column.DATE.Caption").Value);
        Assert.Equal("30", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Stale", details.Plan.Status);
        Assert.Null(details.Plan.AppliedAt);
        Assert.Contains(details.Warnings, w => w.Code == "STALE" && w.Message.Contains("Grid.PageSize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_InsertedMeanwhile_IsStaleExists()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Grid.ShowFilterRow", "true"));
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{p}{Table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.ShowFilterRow', N'false', N'Setting', NULL, N'ALL', 55);");

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("exists", conflict.Reason);
        Assert.Equal("Insert", conflict.Action);
        Assert.Equal("false", conflict.CurrentValue);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Single(rows, r => r.Name == "Grid.ShowFilterRow");
    }

    [Fact]
    public async Task Apply_ServerSettingChanged_ConflictCarriesNoValues()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 11 WHERE NAME = N'Column.DOCNR.Caption';");
        var plan = await PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column=DOCNR"));
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 65 WHERE NAME = N'Grid.Secret';");

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Grid.Secret", conflict.Name);
        Assert.Equal("changed", conflict.Reason);
        Assert.Null(conflict.ExpectedValue);
        Assert.Null(conflict.CurrentValue);
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId.Value);
        Assert.DoesNotContain("abc", string.Join("|", details.Warnings.Select(w => w.Message)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_CreateTable_CreatesAndInserts()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "50", table: "DocumentNewLayout"));

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Applied", result.Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + "DocumentNewLayout");
        var row = Assert.Single(rows);
        Assert.Equal("Grid.PageSize", row.Name);
        Assert.Equal("50", row.Value);
        var columns = await fixture.ScalarOnAsync<string>(Client, $"""
            SELECT STRING_AGG(CONCAT(COLUMN_NAME, ' ', DATA_TYPE, '(', CHARACTER_MAXIMUM_LENGTH, ') ', IS_NULLABLE), ', ')
                   WITHIN GROUP (ORDER BY ORDINAL_POSITION)
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = '{p}DocumentNewLayout'
            """);
        Assert.Equal(
            "NAME nvarchar(200) NO, VALUE nvarchar(400) NO, settingtype nvarchar(20) NO, LISTVALUE nvarchar(1600) YES, workercode nvarchar(100) NO, ordernr int() NO",
            columns);
    }

    [Fact]
    public async Task Apply_FailureMidPlan_RollsBackAndMarksFailed()
    {
        var (p, session) = await NewProcessAsync();
        var plan = await PlanAsync(p, session, Row("Zzz.A", "ok"), Row("Zzz.B", "boom"));
        await PlanSandbox.ExecAsync(fixture, Client, $"ALTER TABLE dbo.{p}{Table} ADD CONSTRAINT CK_{p}_boom CHECK (VALUE <> N'boom');");

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Failed", result.Status);
        Assert.False(string.IsNullOrEmpty(result.Message));
        Assert.Empty(result.Conflicts);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.DoesNotContain(rows, r => r.Name.StartsWith("Zzz.", StringComparison.Ordinal));
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Failed", details.Plan.Status);
        Assert.Equal(result.Message, details.Plan.Message);
        Assert.Null(details.Plan.AppliedAt);
    }

    [Fact]
    public async Task Apply_Renumber_FinalOrderCorrect()
    {
        var (p, session) = await NewProcessAsync();
        await PlanSandbox.ExecAsync(fixture, Client, $"UPDATE dbo.{p}{Table} SET ordernr = 11 WHERE NAME = N'Column.DOCNR.Caption';");
        var plan = await PlanAsync(p, session, Row("Column.DOCNR.Width", "100", after: "Column=DOCNR"));

        var result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);

        Assert.Equal("Applied", result.Status);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal(
            [
                ("Column", "DOCNR", 10),
                ("Column.DOCNR.Width", "100", 20),
                ("Column.DOCNR.Caption", "Dok nr", 30),
                ("Column", "DATE", 40),
                ("Column.DATE.Caption", "Kuupäev", 50),
                ("Grid.PageSize", "20", 60),
                ("Grid.Secret", "abc", 70),
            ],
            rows.Select(r => (r.Name, r.Value, r.OrderNr)).ToArray());
        Assert.Equal("ServerSetting", Assert.Single(rows, r => r.Name == "Grid.Secret").SettingType);
    }

    [Fact]
    public async Task Apply_BackupDeleteDrop_BacksUpDropsAndMarksOriginalRolledBack()
    {
        var (p, session) = await NewProcessAsync();
        var table = p + "DocumentNewLayout";
        var create = await PlanAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "50", table: "DocumentNewLayout"));
        await fixture.Sm.ApplyPlanAsync(Client, create.PlanId!.Value, create.Checksum!);
        var (undoId, undoChecksum) = await InsertUndoPlanAsync(p, table, create.PlanId.Value);

        var result = await fixture.Sm.ApplyPlanAsync(Client, undoId, undoChecksum);

        Assert.Equal("Applied", result.Status);
        Assert.Null(await fixture.ScalarOnAsync<int?>(Client, $"SELECT OBJECT_ID(N'dbo.{table}', N'U')"));
        var undo = await fixture.Sm.GetPlanAsync(Client, undoId);
        var backup = undo.Items[0].BackupTable;
        Assert.NotNull(backup);
        Assert.StartsWith("bck_" + table + "_", backup, StringComparison.Ordinal);
        var backupRows = await PlanSandbox.RowsAsync(fixture, Client, backup);
        Assert.Equal("50", Assert.Single(backupRows).Value);
        var marker = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, backup, "SYSTEM"));
        Assert.Equal("Backup.Description", marker.Name);
        Assert.Equal("BackupDescription", marker.SettingType);
        Assert.Equal(100000, marker.OrderNr);
        Assert.Equal($"Rollback of plan {create.PlanId.Value}: apply", marker.Value);
        Assert.Equal("RolledBack", (await fixture.Sm.GetPlanAsync(Client, create.PlanId.Value)).Plan.Status);
    }

    [Fact]
    public async Task Apply_DropTableWithExtraRow_IsStaleTableNotEmpty()
    {
        var (p, session) = await NewProcessAsync();
        var table = p + "DocumentNewLayout";
        var create = await PlanAsync(p, session, new SmPlanOptions { CreateTable = true }, Row("Grid.PageSize", "50", table: "DocumentNewLayout"));
        await fixture.Sm.ApplyPlanAsync(Client, create.PlanId!.Value, create.Checksum!);
        var (undoId, undoChecksum) = await InsertUndoPlanAsync(p, table, create.PlanId.Value);
        await PlanSandbox.ExecAsync(fixture, Client,
            $"INSERT INTO dbo.{table} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES (N'Grid.Other', N'1', N'Setting', NULL, N'ALL', 99);");

        var result = await fixture.Sm.ApplyPlanAsync(Client, undoId, undoChecksum);

        Assert.Equal("Stale", result.Status);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("table_not_empty", conflict.Reason);
        Assert.Equal(3, conflict.Seq);
        Assert.Equal(2, (await PlanSandbox.RowsAsync(fixture, Client, table)).Count);
        Assert.Equal("Applied", (await fixture.Sm.GetPlanAsync(Client, create.PlanId.Value)).Plan.Status);
        Assert.Equal(0, await fixture.ScalarOnAsync<int>(Client, $"SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'bck[_]{table}[_]%'"));
    }

    [Fact]
    public async Task Apply_SessionTouchFails_ReportsFailed_AndChangesNothing()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var name = "failtouch-" + p;
        var session = await fixture.Sm.StartSessionAsync(Client, name, p);
        var plan = await PlanAsync(p, session, Row("Grid.PageSize", "50"));
        // Makes the session lastactivity touch fail for this session only.
        await PlanSandbox.ExecAsync(fixture, Client, $"""
            CREATE OR ALTER TRIGGER sm.tr_test_failtouch ON sm.session AFTER UPDATE AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE name = N'{name}')
                    THROW 50000, N'session touch failed', 1;
            END
            """);
        SmApplyResult result;
        try
        {
            result = await fixture.Sm.ApplyPlanAsync(Client, plan.PlanId!.Value, plan.Checksum!);
        }
        finally
        {
            await PlanSandbox.ExecAsync(fixture, Client, "DROP TRIGGER IF EXISTS sm.tr_test_failtouch;");
        }

        Assert.Equal("Failed", result.Status);
        Assert.Contains("session touch failed", result.Message, StringComparison.Ordinal);
        var details = await fixture.Sm.GetPlanAsync(Client, plan.PlanId.Value);
        Assert.Equal("Failed", details.Plan.Status);
        Assert.Null(details.Plan.AppliedAt);
        var rows = await PlanSandbox.RowsAsync(fixture, Client, p + Table);
        Assert.Equal("20", Assert.Single(rows, r => r.Name == "Grid.PageSize").Value);
    }

    /// <summary>Stores the inverse of a createTable plan (BackupTable, Delete, DropTable) directly, as Task 7's rollback will.</summary>
    private async Task<(int PlanId, string Checksum)> InsertUndoPlanAsync(string process, string table, int rollbackOf)
    {
        var ordernr = Assert.Single(await PlanSandbox.RowsAsync(fixture, Client, table)).OrderNr;
        var value = await fixture.ScalarOnAsync<string>(Client, $"""
            SET NOCOUNT ON;
            DECLARE @id int;
            INSERT INTO sm.[plan] (sessionid, rollbackof, created, createdby, process, description, source, mode, desired, checksum, status)
            VALUES (NULL, {rollbackOf}, SYSUTCDATETIME(), ORIGINAL_LOGIN(), N'{process}', N'undo', N'test', N'merge', NULL, REPLICATE('0', 64), N'Created');
            SET @id = SCOPE_IDENTITY();
            INSERT INTO sm.planitem (planid, seq, action, physicaltable, [table], layer, workercode, name, value, settingtype, listvalue, ordernr,
                                     oldvalue, oldsettingtype, oldlistvalue, oldordernr)
            VALUES (@id, 1, N'BackupTable', N'{table}', N'DocumentNewLayout', N'base', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
                   (@id, 2, N'Delete', N'{table}', N'DocumentNewLayout', N'base', N'ALL', N'Grid.PageSize', NULL, NULL, NULL, NULL, N'50', N'Setting', NULL, {ordernr}),
                   (@id, 3, N'DropTable', N'{table}', N'DocumentNewLayout', N'base', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
            UPDATE sm.[plan] SET checksum = sm.PlanChecksum(@id) WHERE id = @id;
            SELECT CONCAT(@id, N':', checksum) FROM sm.[plan] WHERE id = @id;
            """);
        var parts = value!.Split(':');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), parts[1]);
    }

    private Task<SmPlanResult> PlanAsync(string process, SmSession session, params SmDesiredRow[] rows) =>
        PlanAsync(process, session, null, rows);

    private async Task<SmPlanResult> PlanAsync(string process, SmSession session, SmPlanOptions? options, params SmDesiredRow[] rows)
    {
        var plan = await fixture.Sm.CreatePlanAsync(Client, new SmDesiredState { Process = process, Rows = rows }, "apply", session.Id, options);
        Assert.Equal("Created", plan.Status);
        return plan;
    }

    private async Task<(string Process, SmSession Session)> NewProcessAsync()
    {
        var p = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "test", p);
        return (p, session);
    }

    private static SmDesiredRow Row(string name, string value, string? after = null, string table = Table) =>
        new() { Table = table, Name = name, Value = value, After = after };
}
