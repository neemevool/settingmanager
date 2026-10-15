namespace VTMF.Settings.Entities;

public class WikiImage
{
    public int Id { get; set; }

    public string Filename { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public DateTime Uploaded { get; set; }
}
