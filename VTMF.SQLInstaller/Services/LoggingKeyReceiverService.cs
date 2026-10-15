using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Services;

public class LoggingKeyReceiverService : IKeyReceiverService
{
    public Task<IEnumerable<InstallTask>> GetInstallTasksAsync(string clientName)
    {
        return Task.FromResult(Enumerable.Empty<InstallTask>());
    }

    public void LogToKeyReceiver(InstallLogs log)
    {
        
    }

    public Task SendSqlSecret(SqlSecret secret)
    {
        return Task.CompletedTask;
    }

    public Task SetInstallationTaskCompleted(string clientName, Guid taskUuid)
    {
        return Task.CompletedTask;
    }
}
