using Microsoft.AspNetCore.Components;

using VTMF.Settings.Entities;
using VTMF.Settings.Models;
using Microsoft.JSInterop;
using BlazorBootstrap;

namespace VTMF.Settings.Components.SettingsManagement;

public partial class SettingNode
{
    [Inject]
    public IJSRuntime JSInterop { get; set; } = null!;

    [Parameter]
    public SettingTreeNode Node { get; set; } = null!;

    [Parameter]
    public EventCallback<SettingTreeNode> OnToggleExpanded { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnDeleteSetting { get; set; }

    [Parameter]
    public EventCallback<(SettingTreeNode Node, string NewValue)> OnValueChanged { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnLoadMetadata { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnLoadFeatures { get; set; }

    [Parameter]
    public EventCallback<SettingNode> OnStartEdit { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnDeleteNode { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnMoveNodeUp { get; set; }

    [Parameter]
    public EventCallback<(SettingTreeNode Node, NewChildArgs args)> OnAddNewChild { get; set; }

    [Parameter]
    public EventCallback<(SettingTreeNode Node, NewSettingArgs args)> OnApplyFeature { get; set; }

    [Parameter]
    public EventCallback<SettingTreeNode> OnSaveDescription { get; set; }

    private bool showInfo = false;
    private bool isEditingDescription = false;
    private string? editingDescriptionValue = null;
    private ConfirmDialog deleteConfirmDialog = default!;

    private string GetNodeKey()
    {
        return Node.FullPath ?? Guid.NewGuid().ToString();
    }
    private string GetNodeClass()
    {
        var classes = new List<string> { "node-item" };

        if (Node.IsLeaf)
        {
            classes.Add("leaf-node");
        }
        else
        {
            classes.Add("branch-node");
        }

        

        return string.Join(" ", classes);
    }
    

    private async Task ToggleExpanded()
    {
        await OnToggleExpanded.InvokeAsync(Node);
    }

    private async Task StartEdit()
    {
        if (Node.Setting == null) return;
        await OnStartEdit.InvokeAsync(this);
        Node.EditValue = Node.Setting.Value;
        Node.IsEditing = true;
        Node.IsAddingNew = false;

        // Load description if not already loaded
        if (Node.Setting != null && ! Node.IsMetadataLoaded)
        {
            await OnLoadMetadata.InvokeAsync(Node);
        }

        //StateHasChanged();
    }

    private void AddNewChild()
    {
        Node.NewSettingName = string.Empty;
        Node.NewSettingValue = string.Empty;
        Node.IsAddingNew = true;
    }
    public Task FinishEditingAsync()
    {
        if (Node.IsEditing && Node.EditValue != null && Node.Setting != null)
        {
            Node.Setting.Value = Node.EditValue;
            Node.IsEditing = false;
            Node.EditValue = null;
        }
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task SaveEdit()
    {
        if (Node.EditValue != null)
        {
            await OnValueChanged.InvokeAsync((Node, Node.EditValue));
        }
        Node.IsEditing = false;
        //StateHasChanged();
    }

    private void CancelEdit()
    {
        Node.IsEditing = false;
        Node.EditValue = null;
        StateHasChanged();
    }

    private async Task SaveNewSetting()
    {
        Node.IsAddingNew = false;
        if (!string.IsNullOrEmpty(Node.NewSettingName) && Node.NewSettingValue != null)
        {
            var args = new NewChildArgs
            {
                SettingName =  Node.NewSettingName,
                SettingValue = Node.NewSettingValue
            };
            await OnAddNewChild.InvokeAsync((Node, args));
        }
        
    }

    private void CancelNewSetting()
    {
        Node.IsAddingNew = false;
    }

    private async Task DeleteNode()
    {
        var confirmation = await deleteConfirmDialog.ShowAsync(
            title: "Want to delete?",
            message1: "Delete: " + Node.FullPath ?? string.Empty,
            message2: "Do you want to proceed?");
        if (!confirmation)
        {
            return;
        }
        if (Node.ParentIsGroup)
        {
            await OnDeleteNode.InvokeAsync(Node);
        }
    }

    private async Task MoveNodeUp()
    {
        if (Node.ParentIsGroup)
        {
            await OnMoveNodeUp.InvokeAsync(Node);
        }
    }

    private async Task DeleteSetting()
    {
        var confirmation = await deleteConfirmDialog.ShowAsync(
            title: "Want to delete?",
            message1: "Delete:" + Node.Setting?.Name ?? string.Empty,
            message2: "Do you want to proceed?");
        if (!confirmation)
        {
            return;
        }
        if (Node.Setting != null)
        {
            await OnDeleteSetting.InvokeAsync(Node);
        }
        
    }
    private async Task ToggleInfo()
    {
        showInfo = !showInfo;

        if (showInfo)
        {
            if (Node.IsLeaf && Node.Setting != null && !Node.IsMetadataLoaded)
                await OnLoadMetadata.InvokeAsync(Node);

            if (!Node.IsLeaf)
                await OnLoadFeatures.InvokeAsync(Node);
        }

        StateHasChanged();
    }

    private async Task ApplyFeature(NewSettingArgs args)
    {
        await OnApplyFeature.InvokeAsync((Node, args));
    }

    private async Task CopyToClipboard()
    {
        if (Node.Setting != null)
        {
            var textToCopy = $"{Node.Setting.Name} => {Node.Setting.Value}";
            await JSInterop.InvokeVoidAsync("navigator.clipboard.writeText", textToCopy);
        }
    }

    private void StartEditingDescription()
    {
        isEditingDescription = true;
        editingDescriptionValue = Node.Setting?.Description ?? string.Empty;
    }

    private async Task SaveDescriptionEdit()
    {
        if (Node.Setting != null && editingDescriptionValue != null)
        {
            Node.Setting.Description = editingDescriptionValue;
            await OnSaveDescription.InvokeAsync(Node);
            isEditingDescription = false;
            editingDescriptionValue = null;
        }
    }

    private void CancelDescriptionEdit()
    {
        isEditingDescription = false;
        editingDescriptionValue = null;
    }
}
