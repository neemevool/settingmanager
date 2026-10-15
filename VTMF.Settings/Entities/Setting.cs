using System.Text.RegularExpressions;
using VTMF.Settings.Services;

namespace VTMF.Settings.Entities;


public class Setting
{
    #region Fields

    private List<Setting> slist = new List<Setting>();

    #endregion Fields

    public Setting()
    {
    }

    public Setting(string name, string? value, string storeTable)
    {
        Name = name;
        Value = value;
        StoreTable = storeTable;
    }

    #region Properties


    public required string Name
    {
        get; set;
    }


    public string? Value
    {
        get; set;
    }

    public SettingValueType ValueType
    {
        get; set;
    }

    public string? FeatureName
    {
        get; set;
    }

    public required string StoreTable { get; set; }
    

    public string? SettingType { get; set; }


    public int OrderNr { get; set; }
    
    public string? Description { get; set; }

    public string? ListValue { get; set; }
    public SettingMeta? Meta
    {
        get; set;
    }

    public bool IsDefault { get; set; } = false;

    #endregion Properties
    public static string SanitizeName(string name)
    {
        return Regex.Replace(name, "[^a-zA-Z0-9_\\.-]", "");
    }

    public static string RemoveTablePrefix(string name, string tablePrefix)
    {
        // remove table prefix only if present
        if (name.StartsWith($"{tablePrefix}."))
            return name.Substring(tablePrefix.Length + 1);
        return name;
    }

    public static string GetTableWithoutPrefix(string storeTable)
    {
        if (storeTable == SettingsService.ServerSettingsTableName)
            return SettingsService.ServerSettingsTableName;
        // remove prefix up to first uppercase letter
        var match = Regex.Match(storeTable, @"^[a-z]+");
        if (match.Success)
            return storeTable.Substring(match.Length);
        return storeTable;
    }

    public static string AppendTablePrefix(string name, string tablePrefix)
    {
        // add table prefix only if not already present
        if (!name.StartsWith($"{tablePrefix}."))
            return $"{tablePrefix}.{name}";
        return name;
    }
    public void AppendTableName(string tablePrefix)
    {
        this.Name = AppendTablePrefix(this.Name, tablePrefix);
    }

}
