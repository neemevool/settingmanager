namespace VTMF.Settings.Entities;

public class ProcessTypeMetainfo
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Label { get; set; }
    public string? Description { get; set; }
    public string Environment { get; set; } = "wterminal";
}
