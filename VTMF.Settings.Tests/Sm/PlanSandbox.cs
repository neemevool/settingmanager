using Microsoft.Data.SqlClient;
using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

/// <summary>Creates throwaway processes and tables in a test client for plan-engine tests.</summary>
internal static class PlanSandbox
{
    private const string TableColumns = "NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL";

    /// <summary>Creates an extra settings table (env or lang overlay) with the given rows, all with <paramref name="workercode"/>.</summary>
    public static async Task CreateLayerTableAsync(SqlServerFixture f, string client, string physicalTable, string workercode,
        params (string Name, string Value, string SettingType, int OrderNr)[] rows)
    {
        var values = string.Join(",\n", rows.Select(r =>
            $"(N'{Esc(r.Name)}', N'{Esc(r.Value)}', N'{Esc(r.SettingType)}', NULL, N'{Esc(workercode)}', {r.OrderNr})"));
        var insert = rows.Length == 0
            ? string.Empty
            : $"INSERT INTO dbo.{QuoteName(physicalTable)} (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES {values};";
        await ExecAsync(f, client, $"CREATE TABLE dbo.{QuoteName(physicalTable)} ({TableColumns});\n{insert}");

        static string Esc(string s) => s.Replace("'", "''", StringComparison.Ordinal);
    }

    public static async Task<string> CreateProcessAsync(SqlServerFixture f, string client, string? process = null, string? settingProcedures = null)
    {
        var p = process ?? "pl" + Guid.NewGuid().ToString("N")[..8];
        const string template = TableColumns;

        await ExecAsync(f, client, $"""
            INSERT INTO dbo.processtypes (NAME, DISPLAYLABEL, TAG, PROCESSTYPE, DATAPROCEDURES, SETTINGPROCEDURES, ENVIRONMENT, ORDERNR)
            VALUES (N'{p}', N'{p}', N'plan', N'TaskDocument', NULL, {(settingProcedures is null ? "NULL" : $"N'{settingProcedures}'")}, N'wterminal', 100);

            CREATE TABLE dbo.{p}DocumentListLayout ({template});
            INSERT INTO dbo.{p}DocumentListLayout (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
             (N'Column',               N'DOCNR',   N'SettingGroup',  NULL, N'ALL', 10),
             (N'Column.DOCNR.Caption', N'Dok nr',  N'Setting',       NULL, N'ALL', 20),
             (N'Column',               N'DATE',    N'SettingGroup',  NULL, N'ALL', 30),
             (N'Column.DATE.Caption',  N'Kuupäev', N'Setting',       NULL, N'ALL', 40),
             (N'Grid.PageSize',        N'20',      N'Setting',       NULL, N'ALL', 50),
             (N'Grid.Secret',          N'abc',     N'ServerSetting', NULL, N'ALL', 60);

            IF OBJECT_ID(N'dbo.ServerProcessSettings', N'U') IS NULL
                CREATE TABLE dbo.ServerProcessSettings ({template});
            INSERT INTO dbo.ServerProcessSettings (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr)
            VALUES (N'{p}.Function.Barcode.Parser', N'Parserbuilder', N'Setting', NULL, N'ALL', 0);
            """);
        return p;
    }

    public static async Task<IReadOnlyList<SmSettingRow>> RowsAsync(SqlServerFixture f, string client, string physicalTable, string workercode = "ALL")
    {
        await using var connection = new SqlConnection(f.Connections.GetConnectionString(client));
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr FROM dbo.{QuoteName(physicalTable)} WHERE workercode = @w ORDER BY ordernr, NAME", connection);
        command.Parameters.AddWithValue("@w", workercode);
        var rows = new List<SmSettingRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SmSettingRow
            {
                Table = physicalTable,
                Layer = reader.GetString(4),
                Name = reader.GetString(0),
                Value = reader.GetString(1),
                SettingType = reader.GetString(2),
                ListValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                OrderNr = reader.GetInt32(5),
            });
        }

        return rows;
    }

    public static async Task ExecAsync(SqlServerFixture f, string client, string sql)
    {
        await using var connection = new SqlConnection(f.Connections.GetConnectionString(client));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteName(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
