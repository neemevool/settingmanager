using Microsoft.AspNetCore.Http.HttpResults;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using VTMF.Settings.Services;
using static System.Net.Mime.MediaTypeNames;

namespace VTMF.Settings.Entities;

public class ProcessType
{
    public const string RelocateItemsValue = "RelocateItems";
    public const string TaskDocumentValue = "TaskDocument";
    public const string DefaultEnvironment = "wterminal";
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string DisplayLabel { get; set; }
    public required string Tag { get; set; }
    public required string ProcessTypeName { get; set; }
    public required string ClientName { get; set; }
    public string? DataProcedures { get; set; }
    public string? SettingProcedures { get; set; }
    public required string Environment { get; set; }
    public bool AutoStart { get; set; }
    public string? Screen { get; set; }
    public int? OrderNr { get; set; }
    public int? DefaultVisible { get; set; }
    public string? WorkerGroups { get; set; }

    public bool IsDefaultEnvironment => Environment == DefaultEnvironment;

    public ClientInfo ClientInfo => new ClientInfo(ClientName);

    public static IReadOnlyList<string> GetProcessTypeNames() => new[] { RelocateItemsValue, TaskDocumentValue };

    public static string SanitizeName(string name)
    {
        return Regex.Replace(name, "[^a-zA-Z0-9_-]", "");
    }

    private static ProcessType defaultTaskProcessType = new ProcessType()
    {
        Id = 0,
        Name = TaskDocumentValue,
        DisplayLabel = TaskDocumentValue,
        Tag = TaskDocumentValue,
        ProcessTypeName = TaskDocumentValue,
        ClientName = SqlConnectionParameters.DefaultMetaBaseClientName,
        Environment = DefaultEnvironment
    };
    private static ProcessType defaultRelocateItemsProcessType = new ProcessType()
    {
        Id = 0,
        Name = RelocateItemsValue,
        DisplayLabel = RelocateItemsValue,
        Tag = RelocateItemsValue,
        ProcessTypeName = RelocateItemsValue,
        ClientName = SqlConnectionParameters.DefaultMetaBaseClientName,
        Environment = DefaultEnvironment
    };
    public static ProcessType DefaultTaskDocumentType => defaultTaskProcessType;
    public static ProcessType DefaultRelocateItemsType => defaultRelocateItemsProcessType;

}
