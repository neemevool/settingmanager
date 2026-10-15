using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface IWikiImageService
{
    Task<(bool success, string message, int id)> UploadAsync(
        string filename,
        string mimeType,
        Stream content,
        long length,
        CancellationToken ct = default);

    Task<WikiImage?> GetByIdAsync(int id, CancellationToken ct = default);
}
