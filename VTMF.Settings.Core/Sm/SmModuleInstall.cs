using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using VTMF.Settings.Services;
using VTMF.SQLInstaller.Services;

namespace VTMF.Settings.Core.Sm;

/// <summary>The install routine shared by the client-module and metabase-module installers: lock, version check, scripts, stamp, one transaction.</summary>
internal static class SmModuleInstall
{
    private const int CommandTimeoutSeconds = 300;

    private static readonly SqlScriptParser Parser = new(NullLogger<SqlScriptParser>.Instance);

    public static async Task<SmInstallResult> RunAsync(SqlConnection connection, string target, SmScriptSet scripts, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AcquireLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            var previous = await ReadInstalledVersionAsync(connection, transaction, scripts.ModuleName, cancellationToken).ConfigureAwait(false);
            var previousState = SmModuleStates.From(previous, scripts.Version);
            if (previousState == SmModuleStates.NewerThanTool)
            {
                throw new SmException(SmErrorCodes.ModuleNewerThanTool,
                    SmMessages.NewerThanTool(target, previous, scripts.Version));
            }

            foreach (var script in scripts.Scripts)
            {
                try
                {
                    foreach (var batch in Parser.SplitIntoBatches(script.Text))
                    {
                        await ExecuteAsync(connection, transaction, batch, null, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (SqlException ex)
                {
                    throw new SmException(SmErrorCodes.InstallFailed, $"Script {script.Name} failed: {ex.Message}", ex);
                }
            }

            await ExecuteAsync(connection, transaction, StampSql,
                new { module = scripts.ModuleName, version = scripts.Version }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new SmInstallResult
            {
                Client = target,
                PreviousState = previousState,
                PreviousVersion = previous,
                InstalledVersion = scripts.Version,
                ScriptsRun = scripts.Scripts.Count,
            };
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private const string StampSql = """
        MERGE sm.moduleinfo AS t
        USING (SELECT @module AS module, @version AS version) AS s
           ON t.module = s.module
        WHEN MATCHED THEN UPDATE SET version = s.version, installed = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT (module, version) VALUES (s.module, s.version);
        """;

    private const string ReadVersionSql = """
        IF OBJECT_ID(N'sm.moduleinfo', N'U') IS NULL
            SELECT CAST(NULL AS int);
        ELSE
            EXEC sp_executesql N'SELECT version FROM sm.moduleinfo WHERE module = @module',
                N'@module nvarchar(128)', @module = @module;
        """;

    private static async Task AcquireLockAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = N'sm.install', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 120000;
            SELECT @r;
            """, transaction: transaction, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (result < 0)
            throw new SmException(SmErrorCodes.InstallFailed, "Could not acquire install lock");
    }


    private static Task<int?> ReadInstalledVersionAsync(SqlConnection connection, SqlTransaction transaction, string moduleName, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int?>(new CommandDefinition(ReadVersionSql, new { module = moduleName },
            transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken));

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, object? parameters, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Transaction already completed or connection broken; nothing to roll back.
        }
        catch (SqlException)
        {
            // Server already rolled the transaction back.
        }
    }
}
