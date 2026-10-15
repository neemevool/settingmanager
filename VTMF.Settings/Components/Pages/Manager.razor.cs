using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Components.Pages;

public partial class Manager
{
    [Parameter]
    public int? ProcessId { get; set; }
    [Parameter]
    public string? ClientName { get; set; }
        
    private bool isLoading = true;
    private string? errorMessage = null;
    protected override async Task OnParametersSetAsync()
    {

        await base.OnParametersSetAsync();

        isLoading = false;
    }

    private void SelectFeatures()
    {
        // Navigate to features management page
        NavigationManager.NavigateTo($"/featuremanager/{ClientName}/{ProcessId}");
    }

    private void SelectSettings()
    {
        // Navigate to settings management page
        NavigationManager.NavigateTo($"/settings/{ClientName}/{ProcessId}");
    }

    private void CopyFromAnotherClient()
    {
        // Navigate to copy settings page
        NavigationManager.NavigateTo($"/copysettings/{ClientName}/{ProcessId}");
    }

    private void BackupSettings()
    {
        // Navigate to backup settings page
        NavigationManager.NavigateTo($"/backupsettings/{ClientName}/{ProcessId}");
    }
}
