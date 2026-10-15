namespace VTMF.Settings.Entities;

public struct TableInfo(string TableName)
{
    public bool IsSettingsaTable => TableName.Equals("Settings", StringComparison.OrdinalIgnoreCase);
    public string TableName { get; } = TableName;
}

