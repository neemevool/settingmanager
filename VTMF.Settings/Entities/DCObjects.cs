namespace VTMF.Settings.Entities;

public class DCObjects
{
    public required string Objectname { get; set; }
    public string? Current { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? Starttime { get; set; }
    public string? Previous { get; set; }
    public DateTime? Fullsync { get; set; }
    public DateTime? Endtime { get; set; }
    public string? Lastexception { get; set; }
}
