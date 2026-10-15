using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;
using VTMF.Settings.Services;

namespace VTMF.Settings.Components.Pages;

public partial class TableManagement
{
    [Parameter]
    public int? ProcessId { get; set; }
    [Parameter]
    public string? ClientName { get; set; }

    private ProcessType? selectedProcessType;
    private List<string> storeTables = new();
    private bool isLoading = true;
    private string? newTableSuffix;
    private bool isCreating = false;

    protected override async Task OnParametersSetAsync()
    {
        if (ProcessId.HasValue && !string.IsNullOrEmpty(ClientName))
        {
            selectedProcessType = await SettingsService.GetProcessTypeById(ClientName, ProcessId.Value);
            await LoadStoreTables();
            isLoading = false;
        }
    }

    private async Task LoadStoreTables()
    {
        if (selectedProcessType == null)
            return;
        storeTables = (await SettingsService.GetStoreTables(selectedProcessType)).ToList();
    }

    private void BackToSettings()
    {
        if (ProcessId.HasValue && !string.IsNullOrEmpty(ClientName))
        {
            NavigationManager.NavigateTo($"/settings/{ClientName}/{ProcessId.Value}");
        }
    }

    private async Task CreateNewTable()
    {
        if (selectedProcessType == null)
            return;

        if (string.IsNullOrWhiteSpace(newTableSuffix))
        {
            ToastService.Notify(new ToastMessage(ToastType.Warning, "Please enter a table name suffix."));
            return;
        }

        isCreating = true;
        try
        {
            var newTableName = $"{selectedProcessType.Name}{newTableSuffix}";
            var (success, message) = await SettingsService.EnsureSettingTable(selectedProcessType, newTableName);

            if (success)
            {
                ToastService.Notify(new ToastMessage(ToastType.Success, message));
                newTableSuffix = string.Empty;
                await LoadStoreTables();
                StateHasChanged();
            }
            else
            {
                ToastService.Notify(new ToastMessage(ToastType.Danger, message));
            }
        }
        catch (Exception ex)
        {
            ToastService.Notify(new ToastMessage(ToastType.Danger, $"Error creating table: {ex.Message}"));
        }
        finally
        {
            isCreating = false;
        }
    }
}
