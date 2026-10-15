using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public record InstallTask(
    Guid UUID,
    string ClientName,
    string TaskName,
    string TaskParameters,
    DateTime TS
);
