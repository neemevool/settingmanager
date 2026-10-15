using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Installers;
public interface IInstaller
{
    Task<IEnumerable<InstalledItem>> InstallAsync(InstallItem installItem, string setName);
}
