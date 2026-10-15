using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;

// <item name="module" check="database" isfolder="true" type="database" source="$/Sawmill/database5/directo/directomodule"/>
public record InstallItem(
    string ItemName,
    string Check,
    bool IsFolder,
    string Type,
    string Source
    
)
{
    public List<InstallItemSpec> Spec { get; init; } = new();
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public InstallMode Mode { get; set; } = InstallMode.Install;
}

public enum InstallMode 
{
    Install,
    Validate,
    Uninstall
}


