using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface IWorkerService
{
    Task<IEnumerable<WorkerProperty>> GetAllAsync(ClientInfo client, CancellationToken ct = default);
    Task<WorkerProperty?> GetByWorkerCodeAsync(ClientInfo client, string workerCode, CancellationToken ct = default);
    Task<(bool success, string message)> CreateAsync(ClientInfo client, WorkerProperty worker, CancellationToken ct = default);
    Task<(bool success, string message)> UpdateAsync(ClientInfo client, WorkerProperty worker, CancellationToken ct = default);
    Task<(bool success, string message)> DeleteAsync(ClientInfo client, string workerCode, CancellationToken ct = default);

    (bool ok, string? error) ValidateStocksCsv(string? stocks);
    (bool ok, string? error) ValidateProcessNamesCsv(string? processNames);

    Task<IEnumerable<SelectOption>> GetStockOptionsAsync(ClientInfo client, CancellationToken ct = default);
    Task<IEnumerable<SelectOption>> GetProcessNameOptionsAsync(ClientInfo client, CancellationToken ct = default);
}
