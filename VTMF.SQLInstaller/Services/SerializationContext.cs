using System.Text.Json.Serialization;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Services;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(InstallLogs))]
[JsonSerializable(typeof(SqlSecret))]
[JsonSerializable(typeof(List<InstallTask>))]
internal partial class SerializationContext : JsonSerializerContext
{
}
