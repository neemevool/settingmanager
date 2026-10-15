
using VTMF.Settings.Entities;

namespace VTMF.Settings.Models;

/// <summary>
/// Represents a node in the settings tree structure
/// </summary>
public class SettingTreeNode
{
    public static readonly string[] GroupSettingPrefixes = new[]
    {
        "Column",
        "Detail",
        "Command",
        "Field",
        "Report",
        "Grid"

    };

    /// <summary>
    /// Prefixes whose SettingGroup rows are identified by [NAME] alone: the row name is the full
    /// group path and [VALUE] holds a caption (e.g. NAME='Detail.Lines', VALUE='Order lines').
    /// Group rows of every other prefix are identified by [NAME]+[VALUE]
    /// (e.g. NAME='Column', VALUE='Key' or NAME='Command', VALUE='OpenDetails').
    /// </summary>
    public static readonly string[] NameKeyedGroupPrefixes = new[]
    {
        "Detail"
    };

    /// <summary>
    /// Whether a setting name or group path belongs to a group whose SettingGroup row is
    /// identified by [NAME] alone. Accepts both a bare group name ("Detail") and a full
    /// group path or setting name ("Detail.Lines").
    /// </summary>
    public static bool IsNameKeyedGroup(string? settingName)
    {
        if (string.IsNullOrEmpty(settingName))
            return false;
        return NameKeyedGroupPrefixes.Contains(settingName.Split('.')[0]);
    }

    /// <summary>
    /// The tree path of the group instance a SettingGroup row represents. This is also the [NAME]
    /// prefix shared by all members of that group instance.
    /// </summary>
    public static string GetGroupPath(Setting groupSetting)
    {
        return IsNameKeyedGroup(groupSetting.Name)
            ? groupSetting.Name
            : $"{groupSetting.Name}.{groupSetting.Value}";
    }
    /// <summary>
    /// The name of this node (the last part after splitting by '.')
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The full path to this node (e.g., "A.B.C")
    /// </summary>
    public string? FullPath { get; set; }

    /// <summary>
    /// The actual setting (null if this is a branch node)
    /// Note: In this implementation, all nodes are leaf nodes since every setting has a value
    /// </summary>
    public Setting? Setting { get; set; }

    /// <summary>
    /// Child nodes in the tree
    /// </summary>
    public List<SettingTreeNode> Children { get; set; } = new();

    /// <summary>
    /// Whether this node is expanded in the UI
    /// </summary>
    public bool IsExpanded { get; set; } = false;

    /// <summary>
    /// Whether this node is a leaf (has a setting with value)
    /// </summary>
    public bool IsLeaf { get; set; } = false;


    /// <summary>
    /// Whether this node is being edited
    /// </summary>
    public bool IsEditing { get; set; } = false;

    public bool IsAddingNew { get; set; } = false;

    /// <summary>
    /// Whether metadata has been loaded for this setting
    /// </summary>
    public bool IsMetadataLoaded { get; set; } = false;

    /// <summary>
    /// Temporary value while editing
    /// </summary>
    public string? EditValue { get; set; }

    public string? NewSettingName { get; set; }
    public string? NewSettingValue { get; set; }

    public bool IsFeaturesLoaded { get; set; } = false;

    public bool ParentIsGroup { get; set; } = false;

    public bool IsGroup { get; set; } = false;

    public string StoreTable { get; set; } = string.Empty;
    public int Level { get; set; }
    public IEnumerable<SettingFeature> Features { get; set; } = Enumerable.Empty<SettingFeature>();

    public string GetWildCardPath()
    {
        // split FullPath by '.' and replace last part with '*'
        var parts = FullPath?.Split('.') ?? Array.Empty<string>();
        if (parts.Length == 0) return "*";
        parts[^1] = "*";
        return string.Join('.', parts);
    }

    public static SettingTreeNodePath GetPathInfo(SettingTreeNode node)
    {
        if (node.FullPath == null)
        {
            throw new ArgumentNullException(nameof(node.FullPath), "Node FullPath cannot be null");
        }
        var nameParts = node.FullPath.Split('.');
        var isRoot = nameParts.Length == 1;
        // use array index syntax to get last element of the array
        var name = nameParts[^1];
        
        var lastDotIndex = node.FullPath.LastIndexOf('.');
        var parentPath = lastDotIndex > 0 ? node.FullPath.Substring(0, lastDotIndex) : string.Empty;
        return new SettingTreeNodePath(name, node.FullPath, parentPath, isRoot, false);
    }
    public static SettingTreeNodePath GetPathInfo(Setting forSetting)
    {
        var nameParts = forSetting.Name.Split('.');
        var isRoot = nameParts.Length == 1;
        // use array index syntax to get last element of the array
        var name = nameParts[^1];
        if (forSetting.SettingType == FeatureTypes.SettingGroup)
        {
            return new SettingTreeNodePath(name, $"{forSetting.Name}.{forSetting.Value}", forSetting.Name, isRoot, true);
        }
        var lastDotIndex = forSetting.Name.LastIndexOf('.');
        var parentPath = lastDotIndex > 0 ? forSetting.Name.Substring(0, lastDotIndex) : string.Empty;
        return new SettingTreeNodePath(name, forSetting.Name, parentPath, isRoot, false);
    }

    public static SettingTreeNodePath? GetParentPathInfo(SettingTreeNodePath forPath)
    {
        if (forPath.IsRoot) return null;
        var nameParts = forPath.ParentPath.Split(".");
        var isRoot = nameParts.Length == 1;
        var name = nameParts[^1];
        var lastDotIndex = forPath.ParentPath.LastIndexOf('.');
        var parentPath = lastDotIndex > 0 ? forPath.ParentPath.Substring(0, lastDotIndex) : string.Empty;
        
        return new SettingTreeNodePath(name, forPath.ParentPath, parentPath, isRoot, false);
    }

    public static (string? prefix, string? node) SplitPathToTwo(string fullPath)
    {
        var lastDotIndex = fullPath.LastIndexOf('.');
        if (lastDotIndex <= 0)
        {
            return (null, null);
        }
        var node = fullPath.Substring(lastDotIndex + 1);
        var prefix = fullPath.Substring(0, lastDotIndex);
        return (prefix, node);
    }

    public static string GetMetaKey(string settingName)
    {
        var nameParts = settingName.Split('.');

        var firstPart = nameParts[0];

        
        if (! SettingTreeNode.GroupSettingPrefixes.Contains(firstPart))
        {
            return settingName;
        }
        return nameParts.Length switch
        {
            1 => nameParts[0],
            2 => nameParts[0] + "." + nameParts[1],
            _ => string.Join(".", nameParts[0..^2]) + "." + nameParts[^1]
        };
    }
}

public struct SettingTreeNodePath(string Name, string FullPath, string ParentPath, bool IsRoot, bool IsGroup)
{
    public string FullPath { get; } = FullPath;
    public string ParentPath { get; } = ParentPath;
    public bool IsGroup { get; } = IsGroup;
    public bool IsRoot { get; } = IsRoot;
    public string Name { get; } = Name;

    public int Level => FullPath.Split('.').Length;
}
