using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Installers;

namespace VTMF.SQLInstaller.Services;
public interface IInstallerFactory
{
    IInstaller GetInstaller(string itemType);
}
