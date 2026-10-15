using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public record InstalledItem
{
    public int ID { get; init; }
    public required string ItemName { get; init; }
    public required string ItemType { get; init; }
    public DateTime InstallTime { get; init; }
    public string? Source { get; init; }
    public string? Target { get; init; }
    public string? SetName { get; init; }
    public required int Version { get; init; }
    public InstallStatus Status { get; set; } = InstallStatus.NotInstalled;
    public string? ErrorMessage { get; set; } = string.Empty;
}

public enum InstallStatus
{
    NotInstalled,
    Skipped,
    Installed,
    Failed,
    ShouldBeInstalled
}
