using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.SqlServer.Server;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml.Linq;
using VTMF.Settings.Components.SettingsManagement;
using VTMF.Settings.Entities;
using VTMF.Settings.Models;
using VTMF.Settings.Services;

namespace VTMF.Settings.Components.Pages;

public partial class SettingsManager
{
    [Parameter]
    public int? ProcessId { get; set; }
    [Parameter]
    public string? ClientName { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private List<ProcessType> processTypes = new();
    private List<string> storeTables = new();
    private ProcessType? selectedProcessType;
    private string? selectedStoreTable;

    private List<Setting> currentSettings = new();
    private List<Setting> availableSettings = new();
    private List<SettingTreeNode> treeRootNodes = new();
    private Dictionary<string, SettingTreeNode> nodes = new();

    private Dictionary<string, SettingDescription?> settingDescriptions = new();
    private readonly Dictionary<string, SettingMeta?> settingValuesLists = new();

    private bool isLoading = true;
    private bool hasChanges = false;
    private string? errorMessage;
    private int totalSettingsCount = 0;
    private SettingNode? currentlyEditingNode;
    
    private bool showManualSettingsForm = false;
    private string? newManualSettings;

    private string? currentSession;
    private string? editingSession;

    protected override async Task OnParametersSetAsync()
    {
        if (ProcessId.HasValue && !string.IsNullOrEmpty(ClientName))
        {
            selectedProcessType = await SettingsService.GetProcessTypeById(ClientName, ProcessId.Value);
            storeTables = (await SettingsService.GetStoreTables(selectedProcessType)).ToList();
            selectedStoreTable = null;
            ClearNodes();
            currentSession = await SettingsService.GetCurrentSettingsSession(selectedProcessType);
            editingSession = currentSession;
            isLoading = false;
        }
        
    }

    private void ClearNodes()
    {
        treeRootNodes.Clear();
        nodes.Clear();
    }

    private async Task SaveSession()
    {
        if (selectedProcessType == null) return;
        var result = await SettingsService.SetCurrentSettingsSession(selectedProcessType, editingSession ?? string.Empty);
        if (result.success)
        {
            currentSession = editingSession;
            ToastService.Notify(new ToastMessage(ToastType.Success, result.message));
        }
        else
        {
            ToastService.Notify(new ToastMessage(ToastType.Danger, result.message));
        }
    }

    private void ClearSubNodes(SettingTreeNode parentNode)
    {
        foreach (var child in parentNode.Children)
        {
            ClearSubNodes(child);
            var removablePathinfo = SettingTreeNode.GetPathInfo(child);
            nodes.Remove(removablePathinfo.FullPath);
            child.Children.Clear();
        }
        parentNode.Children.Clear();
    }

    private async Task OnStartEdit(SettingNode node)
    {
        // If there's already a node being edited, save it first
        if (currentlyEditingNode != null && currentlyEditingNode != node)
        {
            await currentlyEditingNode.FinishEditingAsync();
        }

        currentlyEditingNode = node;
        //StateHasChanged();
    }


    private async Task OnStoreTableChanged(ChangeEventArgs e)
    {
        selectedStoreTable = e.Value?.ToString();
        if (string.IsNullOrEmpty(selectedStoreTable) || selectedProcessType == null)
        {
            ClearNodes();
            return;
        }

        await LoadSettings();
    }

    private async Task LoadSettings()
    {
        if (selectedProcessType == null || string.IsNullOrEmpty(selectedStoreTable))
            return;

        isLoading = true;
        errorMessage = null;

        try
        {
            await SettingsService.ApplyFixes(selectedProcessType, selectedStoreTable);
            var currentTask = SettingsService.GetCurrentSettings(selectedProcessType, selectedStoreTable);
            await Task.WhenAll(currentTask);

            currentSettings = currentTask.Result.ToList();
            
            ClearNodes();
            BuildSettingsTree(currentSettings);

            hasChanges = false;
            totalSettingsCount = CountAllSettings(treeRootNodes);
        }
        catch (Exception ex)
        {
            errorMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void BuildSettingsTree(List<Setting> fromSettings)
    {
        
        var groupSettingPaths = fromSettings
            .Where(s => s.SettingType == FeatureTypes.SettingGroup)
            .ToDictionary(SettingTreeNode.GetGroupPath, s => SettingTreeNode.GetPathInfo(s));
        // Build tree from settings
        foreach (var setting in fromSettings.OrderBy(s => s.OrderNr))
        {
            var settingName = setting.Name;
            var pathInfo = SettingTreeNode.GetPathInfo(setting);

            Console.WriteLine($"{setting.OrderNr}. {settingName}: {pathInfo.FullPath} -> {pathInfo.ParentPath} ");
            
            var parentNode = GetParentTreeNode(pathInfo, groupSettingPaths);

            if (parentNode != null && (setting.SettingType != FeatureTypes.SettingGroup || SettingTreeNode.IsNameKeyedGroup(setting.Name)))
            {
                var level = pathInfo.FullPath.Split('.').Length;
                var isGroup = groupSettingPaths.ContainsKey(pathInfo.FullPath ?? "");
                var node = new SettingTreeNode
                {
                    Name = pathInfo.Name,
                    FullPath = pathInfo.FullPath,
                    Setting = setting,
                    IsLeaf = true,
                    IsGroup = isGroup,
                    ParentIsGroup = parentNode.IsGroup,
                    Level = pathInfo.Level,
                    StoreTable = selectedStoreTable!
                };
                if (nodes.ContainsKey(node.FullPath ?? ""))
                {
                    nodes[node.FullPath ?? ""] = node;
                }
                else
                {
                    nodes.Add(node.FullPath!, node);
                }
                parentNode.Children.Add(node);
            }

            //var parts = settingName.Split('.');
            //InsertIntoTree(treeRootNodes, parts, 0, setting);
        }
        
        int c = nodes.Count;
    }

    private async Task ReloadSubNodes(SettingTreeNode parentNode)
    {
        ArgumentNullException.ThrowIfNull(selectedProcessType);
        var subsettings = await SettingsService.GetSubsSettings(selectedProcessType, parentNode);
        ClearSubNodes(parentNode);
        BuildSettingsTree(subsettings.ToList());
        parentNode.Children.ForEach(n => n.ParentIsGroup = parentNode.IsGroup);
    }

    private SettingTreeNode GetParentTreeNode(SettingTreeNodePath pathInfo, Dictionary<string, SettingTreeNodePath> groupSettingPaths)
    {

        nodes.TryGetValue(pathInfo.ParentPath, out var parentNode);
        if (parentNode != null)
        {
            return parentNode;
        }
        // Column
        // Column.Key.Hidden
        // Name: Hidden
        // FullPath: Column.Key
        
        var nameParts = pathInfo.ParentPath.Split(".");
        var name = nameParts[^1];
        var level = pathInfo.Level - 1;
        if (String.IsNullOrEmpty(name))
        {
            name = "SINGLES";
            level = 1;
        }
        
        var node = new SettingTreeNode
        {
            Name = name,
            FullPath = pathInfo.ParentPath,
            Setting = null,
            IsLeaf = false,
            ParentIsGroup = groupSettingPaths.ContainsKey(pathInfo.ParentPath ?? ""),
            Level = level,
            StoreTable = selectedStoreTable!
        };
        nodes.Add(node.FullPath, node);

        var parentPathInfo = SettingTreeNode.GetParentPathInfo(pathInfo);
        if (parentPathInfo.HasValue && !parentPathInfo.Value.IsRoot)
        {
            var parentParent = GetParentTreeNode(parentPathInfo.Value, groupSettingPaths);
            if (parentParent != null)
            {
                parentParent.Children.Add(node);
            }
        } else
        {
            treeRootNodes.Add(node);
        }
        return node;
    }

    private int CountAllSettings(List<SettingTreeNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.IsLeaf)
            {
                count++;
            }
            count += CountAllSettings(node.Children);
        }
        return count;
    }

    private void OnToggleExpanded(SettingTreeNode node)
    {
        node.IsExpanded = !node.IsExpanded;
        //if (node.IsExpanded && node.IsGroup)
        //{
        //    foreach (var child in node.Children)
        //    {
        //        child.ParentIsGroup = true;
        //    }
        //}
        StateHasChanged();
    }

    private async Task OnSettingValueChanged((SettingTreeNode Node, string NewValue) args)
    {
        var (node, newValue) = args;
        if (node.Setting != null)
        {
            var (success, message) = await SettingsService.SaveSetting(selectedProcessType!, node.Setting, newValue);
            if (success)
            {
                node.Setting.Value = newValue;
                hasChanges = false;
            }
            ShowResultMessage((success, message));
            StateHasChanged();
        }
    }

    private async Task OnLoadMetadata(SettingTreeNode node)
    {

        if (node.Setting == null || selectedProcessType == null)
            return;
        if (node.IsMetadataLoaded)
            return;
        await LoadSettingDescription(node, selectedProcessType);
        await LoadSettingValuesList(node, selectedProcessType);
        node.IsMetadataLoaded = true;
        StateHasChanged();
    }

    private async Task OnLoadFeatures(SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node?.FullPath);
        if (selectedProcessType == null)
            return;
        if (node.IsFeaturesLoaded)
            return;
        try
        {
            var features = await SettingsService.GetSettingFeatures(selectedProcessType, node);
            node.Features = features;
        }
        catch (Exception ex)
        {
            // Features not available
            Console.WriteLine(ex.Message);
        }
        node.IsFeaturesLoaded = true;
        //StateHasChanged();
    }

    private async Task OnAddNewChild((SettingTreeNode Node, NewChildArgs NewChildArgs) args)
    {
        var (   node, childArgs) = args;
        if (selectedProcessType == null)
            return;
        if (node.Setting == null)
            return;
        ReloadOption reload = ReloadOption.None;
        try
        {
            var childAddResult = await SettingsService.AddNewSetting(selectedProcessType, node, childArgs);
            ShowResultMessage((childAddResult.Success, childAddResult.Message));
            reload = childAddResult.Reload;
        }
        
        catch (Exception ex)
        {
            ShowResultMessage((false, ex.Message));
        }
        var nodePathInfo = SettingTreeNode.GetPathInfo(node.Setting);
        if (nodes.TryGetValue(nodePathInfo.ParentPath, out var parentNode))
        {
            try
            {
                await ReloadSubNodes(parentNode);
            }
            catch (Exception ex)
            {
                ShowResultMessage((false, ex.Message));
            }
        }
        
    }
    private async Task OnApplyFeature((SettingTreeNode Node, NewSettingArgs NewSettingArgs) args)
    {
        var (node, featureArgs) = args;
        if (selectedProcessType == null)
            return;

        ReloadOption reload = ReloadOption.None;
        try
        {
            var featureAddResult = await SettingsService.AddNewSetting(selectedProcessType, node, featureArgs);
            ShowResultMessage((featureAddResult.Success, featureAddResult.Message));
            reload = featureAddResult.Reload;
        }
        catch (Exception ex)
        {
            ShowResultMessage((false, ex.Message));
        }
        
        try
        {
            switch (reload)
            {
                case ReloadOption.ReloadAllSettings:
                    await LoadSettings(); break;
                case ReloadOption.ReloadSetting:
                    await ReloadSubNodes(node); break;
            }
        }
        catch (Exception ex)
        {
            ShowResultMessage((false, ex.Message));
        }
        

        //try
        //{
        //    var (success, message) = await SettingsService.ApplyFeatureToSetting(selectedProcessType, node.Setting, featureName);
        //    ShowResultMessage((success, message));
        //    if (success)
        //    {
        //        // Reload the setting value
        //        var updatedSetting = await SettingsService.GetCurrentSettingByName(selectedProcessType, node.Setting.StoreTable, node.Setting.Name);
        //        if (updatedSetting != null)
        //        {
        //            node.Setting.Value = updatedSetting.Value;
        //            hasChanges = false;
        //            StateHasChanged();
        //        }
        //    }
        //}
        //catch (Exception ex)
        //{
        //    ShowErrorMessage($"Error applying feature: {ex.Message}");
        //}
    }

    private async Task LoadSettingDescription(SettingTreeNode node, ProcessType process)
    {
        if (node.Setting == null)
            return;
        var settingDescriptionKey = node.Setting.GetKey(process);
        settingDescriptions.TryGetValue(settingDescriptionKey, out var existingDescription);
        if (existingDescription is null)
        {
            try
            {
                var description = await SettingMetaService.GetSettingDescription(process, node.Setting.Name);
                settingDescriptions.TryAdd(settingDescriptionKey, description);
                node.Setting.Description = description?.Description ?? String.Empty;
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Error loading description: {ex.Message}");
            }
        } else
        {
            node.Setting.Description = existingDescription.Description ?? String.Empty;
        }
    }

    private async Task LoadSettingValuesList(SettingTreeNode node, ProcessType process)
    {
        if (node.Setting == null)
            return;
        var settingValuesKey = node.Setting.GetKey(process);
        if (!settingValuesLists.ContainsKey(settingValuesKey))
        {
            try
            {
                var valuesList = await SettingMetaService.GetSettingValuesLists(process, node.Setting.StoreTable, node.Setting.Name);
                if (valuesList != null)
                {
                    settingValuesLists[settingValuesKey] = valuesList;
                    node.Setting.Meta = valuesList;
                    node.Setting.ValueType = valuesList.SettingValueType;
                }
            }
            catch
            {
                // Values list not available
            }
        }
    }

    private async Task MoveNodeUp(SettingTreeNode node)
    {
        if (selectedProcessType == null)
            return;
        if (node.FullPath == null)
            return;
        var nodePathInfo = SettingTreeNode.GetPathInfo(node);
        if (nodes.TryGetValue(nodePathInfo.ParentPath, out var parentNode))
        {
            var index = parentNode.Children.FindIndex(n => n.FullPath == node.FullPath);
            if (index <= 0)
            {
                ShowSuccessMessage("Is already first");
                return;
            }
            var (success, message) = await SettingsService.MoveSettingGroupNodeUp(selectedProcessType, node);
            ShowResultMessage((success, message));
            if (success)
            {
                // find the previous sibling
                if (index > 0)
                {
                    var temp = parentNode.Children[index - 1];
                    parentNode.Children[index - 1] = node;
                    parentNode.Children[index] = temp;
                }
                StateHasChanged();
            }
        }
        else
        {
            ShowErrorMessage("Parent node not found.");

        }
    }
    private async Task DeleteNode(SettingTreeNode node)
    {
        if (selectedProcessType == null)
            return;
        if (node.FullPath == null)
            return;
        var (success, message) = await SettingsService.DeleteNode(selectedProcessType, node);
        ShowResultMessage((success, message));
        if (success)
        {
            RemoveSettingFromTree(node);
            hasChanges = false;
            totalSettingsCount = CountAllSettings(treeRootNodes);
            StateHasChanged();
        }
        return;
    }
    private async Task DeleteSetting(SettingTreeNode node)
    {
        if (node.Setting == null || selectedProcessType == null)
            return;
        var (success, message) = await SettingsService.DeleteSetting(selectedProcessType, node.Setting);
        ShowResultMessage((success, message));
        if (success)
        {
            // Remove the setting from the tree
            RemoveSettingFromTree(node);
            hasChanges = false;
            totalSettingsCount = CountAllSettings(treeRootNodes);
            StateHasChanged();
        }
        return;
    }

    private async Task OnSaveDescription(SettingTreeNode node)
    {
        if (node.Setting == null || selectedProcessType == null)
            return;
        if (string.IsNullOrEmpty(node.Setting.Description)) 
            return;
        SettingDescription description = new SettingDescription(node.Setting.Name, node.Setting.Description);

        try
        {
            var result = await SettingMetaService.UpdateSettingDescription(selectedProcessType, description);
            ShowResultMessage((result.success, result.message));
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Error saving description: {ex.Message}");
        }
    }

    private bool RemoveSettingFromTree(SettingTreeNode node)
    {
        // A SettingGroup row is rendered as a leaf below its group container (its tree path is
        // Name.Value, while the group instance lives at the container path). Deleting that row
        // deletes the whole group instance, so the container and its members have to go as well.
        var removablePath = node.Setting?.SettingType == FeatureTypes.SettingGroup
            ? SettingTreeNode.GetGroupPath(node.Setting)
            : node.FullPath;

        if (removablePath == null)
        {
            return false;
        }

        // find the node which is parent of the deleted node
        var parentNode = nodes.FirstOrDefault(n => n.Value.Children.Any(c => c.FullPath == removablePath));
        // remove from parent children the node
        if (parentNode.Value != null)
        {
            parentNode.Value.Children.RemoveAll(c => c.FullPath == removablePath);
        }
        // the deleted node can be a root node, those are not referenced as children anywhere
        treeRootNodes.RemoveAll(n => n.FullPath == removablePath);
        // Remove the setting from the tree
        var removable = nodes.Where(n => n.Key.StartsWith(removablePath + ".") || n.Key == removablePath).Select(n => n.Key).ToList();

        foreach (var key in removable)
        {
            nodes.Remove(key);
        }
        return true;
    }
    private void ExpandAll()
    {
        ExpandAllNodes(treeRootNodes);
        StateHasChanged();
    }

    private void AddSettingsManually()
    {
        showManualSettingsForm = true;
        newManualSettings = string.Empty;
        StateHasChanged();
    }

    private void CloseManualSettingsForm()
    {
        showManualSettingsForm = false;
        newManualSettings = string.Empty;
        StateHasChanged();
    }

    private async Task SaveManualSettings()
    {
        if (selectedProcessType == null || string.IsNullOrEmpty(selectedStoreTable))
        {
            ShowErrorMessage("Please select a process type and store table first.");
            return;
        }

        if (string.IsNullOrEmpty(newManualSettings))
        {
            ShowErrorMessage("Please enter settings data.");
            return;
        }
        var tableInfo = new TableInfo(selectedStoreTable);
        var parents = new HashSet<string>();
        try
        {
            var settinglines = newManualSettings.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in settinglines)
            {
                var parts = line.Split(new[] { "=>","=","->" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var settingType = line.Contains("->") ? "SettingGroup" : "Setting";
                    var settingName = parts[0].Trim();
                    var settingValue = parts[1].Trim();
                    // Call your service to save each setting
                    var result = await SettingsService.AddNewSetting(selectedProcessType, tableInfo, settingName, settingValue, settingType);
                    if (result.Success && result.ParentSettingName != null)
                    {
                        parents.Add(result.ParentSettingName);
                    }
                }
            }
            ShowSuccessMessage("Settings saved successfully.");
            CloseManualSettingsForm();
            if (parents.Count == 0)
            {
                await ReloadSettings();
                return;
            }
            foreach (var parentName in parents)
            {
                if (nodes.TryGetValue(parentName, out var parentNode))
                {
                    await ReloadSubNodes(parentNode);
                }
            }

        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Error saving settings: {ex.Message}");
        }
    }

    private void ExpandAllNodes(List<SettingTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = true;
            if (node.Children.Any())
            {
                ExpandAllNodes(node.Children);
            }
        }
    }

    private void CollapseAll()
    {
        CollapseAllNodes(treeRootNodes);
        StateHasChanged();
    }

    private void CollapseAllNodes(List<SettingTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = false;
            if (node.Children.Any())
            {
                CollapseAllNodes(node.Children);
            }
        }
    }

    private async Task SaveChanges()
    {
        // TODO: Implement save functionality
        // This would call a service method to persist the changes
        errorMessage = "Save functionality not yet implemented. Connect to your save API endpoint.";
        await Task.CompletedTask;
    }

    private async Task ReloadSettings()
    {
        await LoadSettings();
    }

    private void ManageTables()
    {
        if (ProcessId.HasValue && !string.IsNullOrEmpty(ClientName))
        {
            NavigationManager.NavigateTo($"/table-management/{ClientName}/{ProcessId.Value}");
        }
    }

    private void ShowResultMessage((bool Success, string Message) result)
    {
        if (result.Success)
        {
            ShowSuccessMessage(result.Message ?? String.Empty);
        }
        else
        {
            ShowErrorMessage(result.Message ?? String.Empty);
        }
    }
    private void ShowSuccessMessage(string message) => ShowMessage(ToastType.Success, message);
    private void ShowErrorMessage(string message) => ShowMessage(ToastType.Danger, message);
    private void ShowMessage(ToastType toastType, string message) => ToastService.Notify(new ToastMessage(toastType, message));
    

}
