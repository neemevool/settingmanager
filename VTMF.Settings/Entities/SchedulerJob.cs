namespace VTMF.Settings.Entities;

public class SchedulerJob
{
    //name,query,workercode,paramsxml,auth,seconds,cron,disabled,datastore,reset,fromhours,tohours
    public required string Name { get; set; }
    public required string Query { get; set; }
    public string? WorkerCode { get; set; }
    public string? ParamsXml { get; set; }
    public string? Auth { get; set; }
    public int? Seconds { get; set; }
    public string? Cron { get; set; }
    public bool Disabled { get; set; }
    public string? Datastore { get; set; }
    public bool Reset { get; set; }
    public int FromHours { get; set; }
    public int ToHours { get; set; }

    public bool HoursNotSupported { get; set; }
}
