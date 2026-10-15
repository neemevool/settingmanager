using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Services;

public interface IKeyReceiverService
{
    Task<IEnumerable<InstallTask>> GetInstallTasksAsync(string clientName);
    void LogToKeyReceiver(InstallLogs log);
    Task SendSqlSecret(SqlSecret secret);
    Task SetInstallationTaskCompleted(string clientName, Guid taskUuid);
}