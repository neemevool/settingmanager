using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface ISchedulerService
{
    Task<IEnumerable<SchedulerJob>> GetAllJobsAsync(ClientInfo client, CancellationToken cancellationToken = default);
    Task<SchedulerJob?> GetJobByNameAsync(ClientInfo client, string jobName, CancellationToken cancellationToken = default);
    Task<(bool success, string message)> EnableJobsAsync(ClientInfo client, IEnumerable<string> jobNames, CancellationToken cancellationToken = default);
    Task<(bool success, string message)> DisableJobsAsync(ClientInfo client, IEnumerable<string> jobNames, CancellationToken cancellationToken = default);
    Task<(bool success, string message)> RegenerateJobsAsync(ClientInfo client, CancellationToken cancellationToken = default);

    Task<(bool success, string message)> UpdateSchedulerJob(ClientInfo clientInfo, SchedulerJob schedulerJob, CancellationToken cancellationToken = default);

    Task<IEnumerable<DCObjects>> GetAllDCObjects(ClientInfo client, CancellationToken cancellationToken = default);
    Task<DCObjects?> GetDCObjectByName(ClientInfo client, string objectName, CancellationToken cancellationToken = default);
    Task<(bool success, string message)> ResetDCObject(ClientInfo client, string objectName, int? setUpdatedDaysBack, CancellationToken cancellationToken = default);
}
