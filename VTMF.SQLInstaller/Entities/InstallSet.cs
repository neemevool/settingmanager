using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public record InstallSet(string Name, bool IsOptional)
{
    public List<InstallItem> Items { get; init; } = new();
}


