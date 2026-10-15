using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class WikiImageService(SqlConnectionProvider sqlConnectionProvider) : IWikiImageService
{
    private const string TableName = "settingswikiimage";
    private const long MaxBytes = 3 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    private const string SelectColumns = """
        id AS Id,
        filename AS Filename,
        mimetype AS MimeType,
        content AS Content,
        uploaded AS Uploaded
        """;

    private const string EnsureTableSql = $"""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{TableName}')
        BEGIN
            CREATE TABLE [dbo].[{TableName}] (
                [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [filename] NVARCHAR(255) NOT NULL,
                [mimetype] NVARCHAR(100) NOT NULL,
                [content] VARBINARY(MAX) NOT NULL,
                [uploaded] DATETIME NOT NULL
            )
        END
        """;

    private bool _tableEnsured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    private async Task EnsureTableAsync(SqlConnection connection, CancellationToken ct)
    {
        if (_tableEnsured)
            return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_tableEnsured)
                return;
            await connection.ExecuteAsync(new CommandDefinition(EnsureTableSql, cancellationToken: ct));
            _tableEnsured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public async Task<(bool success, string message, int id)> UploadAsync(
        string filename,
        string mimeType,
        Stream content,
        long length,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return (false, "Filename is required.", 0);

        if (length <= 0)
            return (false, "File is empty.", 0);

        if (length > MaxBytes)
            return (false, $"File exceeds the {MaxBytes / (1024 * 1024)} MB limit.", 0);

        var extension = Path.GetExtension(filename);
        if (!AllowedExtensions.Contains(extension))
            return (false, "Only .png, .jpg and .jpeg files are allowed.", 0);

        if (!AllowedMimeTypes.Contains(mimeType))
            return (false, "Only PNG and JPEG images are allowed.", 0);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await content.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        if (bytes.Length == 0)
            return (false, "File is empty.", 0);

        if (bytes.Length > MaxBytes)
            return (false, $"File exceeds the {MaxBytes / (1024 * 1024)} MB limit.", 0);

        var detectedMime = DetectMimeFromMagic(bytes);
        if (detectedMime is null)
            return (false, "File contents do not match a supported image format.", 0);

        if (!string.Equals(detectedMime, mimeType, StringComparison.OrdinalIgnoreCase))
            return (false, "File contents do not match the declared image type.", 0);

        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);

        var sql = $"""
            INSERT INTO {TableName} (filename, mimetype, content, uploaded)
            OUTPUT INSERTED.id
            VALUES (@Filename, @MimeType, @Content, @Uploaded)
            """;

        var parameters = new
        {
            Filename = filename,
            MimeType = detectedMime,
            Content = bytes,
            Uploaded = DateTime.UtcNow
        };

        var newId = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return newId > 0
            ? (true, "Image uploaded.", newId)
            : (false, "Image was not stored.", 0);
    }

    public async Task<WikiImage?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = sqlConnectionProvider.GetDefaultMetaBaseConnection();
        await EnsureTableAsync(connection, ct);
        var sql = $"SELECT {SelectColumns} FROM {TableName} WHERE id = @id";
        return await connection.QuerySingleOrDefaultAsync<WikiImage>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    private static string? DetectMimeFromMagic(byte[] bytes)
    {
        if (StartsWith(bytes, PngMagic))
            return "image/png";
        if (StartsWith(bytes, JpegMagic))
            return "image/jpeg";
        return null;
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
                return false;
        }
        return true;
    }
}
