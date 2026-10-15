using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public record Install
{
    public List<InstallSet> Sets { get; init; } = new();
}
