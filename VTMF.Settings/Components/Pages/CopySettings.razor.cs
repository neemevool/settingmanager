using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;
using VTMF.Settings.Services;

namespace VTMF.Settings.Components.Pages;

public partial class CopySettings
{
    [Parameter]
    public int ProcessId { get; set; }

    [Parameter]
    public string? ClientName { get; set; }

    

    private List<string> availableClients = new();
    private string? selectedSourceClient;
    private List<string> availableStoreTables = new();
    private Dictionary<string, bool> selectedTables = new();
    private bool selectAllTables = false;
    private bool isLoadingClients = true;
    private bool isLoadingTables = false;
    private string? errorMessage;
    private ClientInfo? sourceClientInfo;
    private ProcessType? selectedProcessType;
    private ProcessType? sourceProcessType;
    private bool isCopying = false;
    private int currentProgress = 0;
    private string? currentCopyingTable;
    private List<string> allProcessTypeNames = new();


    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (ProcessId!=0 && !string.IsNullOrEmpty(ClientName))
        {
            selectedProcessType = await SettingsService.GetProcessTypeById(ClientName, ProcessId);
            
        }
        try
        {
            // Get all available clients except the current one
            var allClients = SqlConnectionProvider.GetAvailableClients().ToList();
            availableClients = allClients.Where(c => c != ClientName).ToList();


            isLoadingClients = false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error loading clients: {ex.Message}";
            isLoadingClients = false;
        }
    }

    private async Task OnSourceProcessChanged(string processName)
    {
        ArgumentNullException.ThrowIfNull(selectedSourceClient);
        if (string.IsNullOrEmpty(processName))
        {
            return;
        }
        sourceProcessType = await SettingsService.GetProcessTypeByName(selectedSourceClient, processName);
        await LoadSourceTables(sourceProcessType);
    }
    private async Task OnSourceClientChanged(string clientName)
    {
        ArgumentNullException.ThrowIfNull(selectedProcessType);
        ArgumentNullException.ThrowIfNull(ClientName);
        selectedSourceClient = clientName;
        selectedTables.Clear();
        selectAllTables = false;
        availableStoreTables.Clear();
        errorMessage = null;
        selectedProcessType = await SettingsService.GetProcessTypeById(ClientName, ProcessId);
        bool flowControl = await LoadSourceTables(selectedProcessType);
        if (!flowControl)
        {
            return;
        }
    }

    private async Task<bool> LoadSourceTables(ProcessType fromProcessType)
    {
        if (string.IsNullOrEmpty(selectedSourceClient))
        {
            return false;
        }
        

        try
        {
            isLoadingTables = true;
            var sourceProcessType = await SettingsService.GetProcessTypeByName(selectedSourceClient, fromProcessType.Name);

            sourceClientInfo = sourceProcessType.ClientInfo;
            var tables = await SettingsService.GetStoreTables(sourceProcessType);
            availableStoreTables = tables.ToList();

            // Initialize all tables as unchecked
            foreach (var table in availableStoreTables)
            {
                selectedTables[table] = false;
            }
            var allProcessTypes = await SettingsService.GetProcessTypes(selectedSourceClient);
            allProcessTypeNames = allProcessTypes.Where(p => p.ProcessTypeName == fromProcessType.ProcessTypeName).Select(pt => pt.Name).ToList();
        }
        catch (Exception ex)
        {
            errorMessage = $"Error loading tables: {ex.Message}";
        }
        finally
        {
            isLoadingTables = false;
        }

        return true;
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

    private async Task OnCopySettings()
    {
        if (sourceClientInfo == null || selectedProcessType == null)
        {
            ShowMessage(ToastType.Danger, "Source client or process type is not selected.");
            
            return;
        }
        // Get selected tables
        var selectedTablesList = selectedTables
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        isCopying = true;

        if (!selectedTablesList.Any())
        {
            ShowMessage(ToastType.Warning, "Please select at least one table to copy.");
            return;
        }

        foreach (var table in selectedTablesList)
        {
            try
            {
                currentCopyingTable = table;
                currentProgress = 0;
                var targetProcessType = await SettingsService.GetProcessTypeById(ClientName!, ProcessId);

                var progress = new Progress<int>(value =>
                {
                    currentProgress = value;
                    InvokeAsync(StateHasChanged);
                });

                
                var (success, message) = await SettingsService.CopySettings(selectedProcessType, sourceClientInfo.Value, table, progress);
                ShowMessage(success ? ToastType.Success : ToastType.Danger, message);
                currentProgress = 0;
                selectedTables.Remove(table);
                StateHasChanged();
            }
            catch (Exception ex)
            {
                ShowMessage(ToastType.Danger, $"Error copying settings for table {table}: {ex.Message}");
            }
        }
        isCopying = false;
        currentProgress = 0;
        currentCopyingTable = null;
        //await InvokeAsync(StateHasChanged);
    }

    private void OnCancel()
    {
        NavigationManager.NavigateTo($"/manager/{ClientName}/{ProcessId}");
    }

    private void ShowMessage(ToastType toastType, string message) => ToastService.Notify(new ToastMessage(toastType, message));
}
