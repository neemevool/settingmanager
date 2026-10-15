using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Entities;
public class InstallConfig
{
    public required string ClientName { get; init; }
    public required string SqlScriptsDir { get; init; }
    public required string ConnectionString { get; init; }
    public required string InstallXmlPath { get; init; }
    public required string DatabaseName { get; init; }
    public required string InstallMode { get; init; }
    public required string SqlScriptsPrefix { get; init; } = "";
    public required string ConfigDir { get; init; }
    public string? KeyReceiverUrl { get; init; }
    public string? KeyReceiverToken { get; init; }
    public string? Admins { get; init; }
    public string? Sets { get; init; }
    public string? TfsToken { get; init; }
}
