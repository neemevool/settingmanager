using Dapper;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class SchedulerService(SqlConnectionProvider sqlConnectionProvider) : ISchedulerService
{
    public async Task<IEnumerable<SchedulerJob>> GetAllJobsAsync(ClientInfo client, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT 
                name AS Name, query AS Query, workercode AS WorkerCode, paramsxml AS ParamsXml, auth AS Auth, seconds AS Seconds, cron AS Cron, disabled AS Disabled, datastore AS Datastore, reset AS Reset, fromhours AS FromHours, tohours AS ToHours FROM schedulerjobs
            """;

        try
        {
            var jobs = await connection.QueryAsync<SchedulerJob>(new CommandDefinition(sql, cancellationToken: cancellationToken));
            return jobs;
        } catch (Exception ex)
        {
            if (ex.Message.Contains("fromhours")) // fromhours column missing
            {
                const string sqlwohours = """
                SELECT 
                    name AS Name, query AS Query, workercode AS WorkerCode, paramsxml AS ParamsXml, auth AS Auth, seconds AS Seconds, cron AS Cron, disabled AS Disabled, datastore AS Datastore, reset AS Reset, null AS FromHours, null AS ToHours FROM schedulerjobs
                """;
                var jobs = await connection.QueryAsync<SchedulerJob>(new CommandDefinition(sqlwohours, cancellationToken: cancellationToken));
                return jobs;
            }
            else
            {
                return Array.Empty<SchedulerJob>();
            }
        }
        
    }

    public async Task<SchedulerJob?> GetJobByNameAsync(ClientInfo client, string jobName, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT 
                name AS Name, query AS Query, workercode AS WorkerCode, paramsxml AS ParamsXml, auth AS Auth, seconds AS Seconds, cron AS Cron, disabled AS Disabled, datastore AS Datastore, reset AS Reset, fromhours AS FromHours, tohours AS ToHours FROM schedulerjobs
            WHERE name = @jobName
            """;
        try
        {
            var job = await connection.QuerySingleOrDefaultAsync<SchedulerJob>(new CommandDefinition(sql, new { jobName }, cancellationToken: cancellationToken));
            return job;
        } catch (Exception ex)
        {
            if (ex.Message.Contains("fromhours")) // fromhours column missing
            {
                const string sqlwohours = """
                SELECT 
                    name AS Name, query AS Query, workercode AS WorkerCode, paramsxml AS ParamsXml, auth AS Auth, seconds AS Seconds, cron AS Cron, disabled AS Disabled, datastore AS Datastore, reset AS Reset, null AS FromHours, null AS ToHours FROM schedulerjobs
                WHERE name = @jobName
                """;
                var job = await connection.QuerySingleOrDefaultAsync<SchedulerJob>(new CommandDefinition(sqlwohours, new { jobName }, cancellationToken: cancellationToken));
                if (job != null)
                    job.HoursNotSupported = true;
                return job;
            }
            else
            {
                return null;
            }
        }

        
    }

    public async Task<(bool success, string message)> EnableJobsAsync(ClientInfo client, IEnumerable<string> jobNames, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            UPDATE schedulerjobs SET disabled = 0,reset = 1 WHERE name IN @jobNames and disabled = 1 and reset=0
            """;

        var result = await connection.ExecuteAsync(new CommandDefinition(sql, new { jobNames }, cancellationToken: cancellationToken));
        return (result > 0, result > 0 ? "Jobs enabled successfully." : "No jobs were enabled.");
    }

    public async Task<(bool success, string message)> DisableJobsAsync(ClientInfo client, IEnumerable<string> jobNames, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            UPDATE schedulerjobs SET disabled = 1,reset=1 WHERE name IN @jobNames and disabled = 0 and reset=0
            """;
        var result = await connection.ExecuteAsync(new CommandDefinition(sql, new { jobNames }, cancellationToken: cancellationToken));
        return (result > 0, result > 0 ? "Jobs disabled successfully." : "No jobs were disabled.");
    }

    public async Task<(bool success, string message)> RegenerateJobsAsync(ClientInfo client, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            EXEC dbo.GenerateSchedulerJobs
            """;
        var result = await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return (result >= 0, "Scheduler jobs regenerated successfully.");
    }

    public async Task<(bool success, string message)> UpdateSchedulerJob(ClientInfo clientInfo, SchedulerJob schedulerJob, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(clientInfo.ClientName);
        var sql = """
            UPDATE schedulerjobs SET 
                seconds = nullif(@Seconds,0),
                cron = nullif(@Cron,''),
                reset = 1,
                fromhours = nullif(@FromHours,0),
                tohours = nullif(@ToHours,0)
            WHERE name = @Name
            """;
        if (schedulerJob.HoursNotSupported)
        {
            sql = """
            UPDATE schedulerjobs SET 
                seconds = nullif(@Seconds,0),
                cron = nullif(@Cron,''),
                reset = 1
            WHERE name = @Name
            """;
        }
        
        var result = await connection.ExecuteAsync(new CommandDefinition(sql, schedulerJob, cancellationToken: cancellationToken));
        return (result > 0, result > 0 ? "Scheduler job updated successfully." : "No scheduler job was updated.");
    }

    public async Task<IEnumerable<DCObjects>> GetAllDCObjects(ClientInfo client, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT 
                objectname AS Objectname, cast([current] as nvarchar(36)) AS [Current], updated AS Updated, starttime AS Starttime, cast([previous] as nvarchar(36))  AS [Previous], fullsync AS Fullsync, endtime AS Endtime, lastexception AS Lastexception FROM dcobjects
            """;
        var dcObjects = await connection.QueryAsync<DCObjects>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return dcObjects;
    }

    public async Task<DCObjects?> GetDCObjectByName(ClientInfo client, string objectName, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            SELECT 
                objectname AS Objectname, cast([current] as nvarchar(36)) AS [Current], updated AS Updated, starttime AS Starttime, cast([previous] as nvarchar(36))  AS [Previous], fullsync AS Fullsync, endtime AS Endtime, lastexception AS Lastexception FROM dcobjects
            WHERE objectname = @objectName
            """;
        var dcObject = await connection.QuerySingleOrDefaultAsync<DCObjects>(new CommandDefinition(sql, new { objectName }, cancellationToken: cancellationToken));
        return dcObject;
    }

    public async Task<(bool success, string message)> ResetDCObject(ClientInfo client, string objectName, int? setUpdatedDaysBack, CancellationToken cancellationToken = default)
    {
        await using var connection = sqlConnectionProvider.GetConnection(client.ClientName);
        const string sql = """
            UPDATE dcobjects SET 
                updated = GETDATE()-@setUpdatedDaysBack,
                endtime = GETDATE(),
                lastexception = NULL
            WHERE objectname = @objectName
            """;
        var result = await connection.ExecuteAsync(new CommandDefinition(sql, new { objectName, setUpdatedDaysBack }, cancellationToken: cancellationToken));
        return (result > 0, result > 0 ? "DC Object reset successfully." : "No DC Object was reset.");
    }
}
