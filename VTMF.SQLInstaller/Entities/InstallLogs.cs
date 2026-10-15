using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public record InstallLogs
{
    public DateTime TS { get; set; } = DateTime.Now;
    public string ClientName { get; set; } = string.Empty;
    public string LogLevel { get; set; } = string.Empty;
    public string LogMessage { get; set; } = string.Empty;
}