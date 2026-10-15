using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;
using VTMF.Settings.Services;

namespace VTMF.Settings.Components.Pages;

public partial class BackupSettings
{
    [Parameter]
    public int ProcessId { get; set; }

    [Parameter]
    public string? ClientName { get; set; }
    private ProcessType? selectedProcessType;
    private List<string> availableStoreTables = new();
    private List<string> availableBackups = new();
    private Dictionary<string, bool> selectedTables = new();
    private bool selectAllTables = false;
    private bool isLoadingTables = false;
    private string? errorMessage;
    private string backupDescription = "";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (ProcessId != 0 && !string.IsNullOrEmpty(ClientName))
        {
            selectedProcessType = await SettingsService.GetProcessTypeById(ClientName, ProcessId);
        }
        selectedTables.Clear();
        selectAllTables = false;
        availableStoreTables.Clear();
        errorMessage = null;

        try
        {
            ArgumentNullException.ThrowIfNull(selectedProcessType);
            isLoadingTables = true;
            var tables = await SettingsService.GetStoreTables(selectedProcessType);
            availableStoreTables = tables.ToList();
            await LoadAvailableBackups();
            // Initialize all tables as unchecked
            foreach (var table in availableStoreTables)
            {
                selectedTables[table] = false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error loading tables: {ex.Message}";
        }
        finally
        {
            isLoadingTables = false;
        }
    }

    private void OnSelectAllChanged(ChangeEventArgs e)
    {
        selectAllTables = (bool)e.Value!;
        var tablesToUpdate = availableStoreTables.ToList();
        foreach (var table in tablesToUpdate)
        {
            selectedTables[table] = selectAllTables;
        }
    }

    private void OnTableCheckboxChanged(string tableName, ChangeEventArgs e)
    {
        selectedTables[tableName] = (bool)e.Value!;

        // Update selectAll checkbox if all/none are selected
        selectAllTables = availableStoreTables.All(t => selectedTables.ContainsKey(t) && selectedTables[t]);
    }

    private async Task BackupSelectedTables()
    {
        if (selectedProcessType == null)
        {
            ShowMessage(ToastType.Danger, "Process type is not selected.");
            return;
        }
        var tablesToBackup = selectedTables.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        if (tablesToBackup.Count == 0)
        {
            ShowMessage(ToastType.Danger, "No tables selected for backup.");
            return;
        }
        try
        {
            foreach (var table in tablesToBackup)
            {
                var description = !string.IsNullOrWhiteSpace(backupDescription) ? backupDescription : "Backup via UI";
                var (success, message) = await SettingsService.BackupSettings(selectedProcessType, table, description);
                ShowMessage(success ? ToastType.Success : ToastType.Danger, message);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ToastType.Danger, $"Error during backup: {ex.Message}");
        }
    }

    private async Task LoadAvailableBackups()
    {
        if (selectedProcessType == null)
        {
            ShowMessage(ToastType.Danger, "Process type is not selected.");
            return;
        }
        try
        {
            availableBackups = (await SettingsService.GetBackupTables(selectedProcessType)).ToList();
        }
        catch (Exception ex)
        {
            ShowMessage(ToastType.Danger, $"Error loading backups: {ex.Message}");
        }
    }
    private void OnCancel()
    {
        NavigationManager.NavigateTo($"/manager/{ClientName}/{ProcessId}");
    }

    private void ShowMessage(ToastType toastType, string message) => ToastService.Notify(new ToastMessage(toastType, message));     
}
