using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface IWikiService
{
    Task<IEnumerable<WikiPage>> GetAllAsync(CancellationToken ct = default);
    Task<WikiPage?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<(bool success, string message, int id)> CreateAsync(WikiPage page, CancellationToken ct = default);
    Task<(bool success, string message)> UpdateAsync(WikiPage page, CancellationToken ct = default);
    Task<(bool success, string message)> DeleteAsync(int id, CancellationToken ct = default);
}
