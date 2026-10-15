using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;
using VTMF.Settings.Services;

namespace VTMF.Settings.Components.Pages;

public partial class FeatureManager
{
    [Parameter]
    public string? ProcessTypeName { get; set; }
    [Parameter]
    public string? ClientName { get; set; }

    private bool isLoading = true;
    private string? errorMessage;
    private ProcessType? selectedProcessType;
    private IEnumerable<SettingFeatureGroup>? featureGroups;
    private readonly Dictionary<string, FeatureState> featureStates =  new();
    private readonly Dictionary<string, IEnumerable<FeaturePrompt>> featurePrompts = new();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!string.IsNullOrEmpty(ProcessTypeName) && !string.IsNullOrEmpty(ClientName))
        {
            selectedProcessType = await SettingsService.GetProcessTypeByName(ClientName, ProcessTypeName);
            if (selectedProcessType != null)
            {
                featureGroups = await SettingMetaService.GetSettingFeatureGroups(selectedProcessType);
            }
            isLoading = false;
            errorMessage = null;
        }
    }

    private async Task ApplyFeature(SettingFeatureGroup featureGroup)
    {
        if (featureGroup == null)
        {
            errorMessage = "Invalid feature selected.";
            return;
        }

        try
        {
            // Update state to Pending
            featureStates[featureGroup.GroupName] = FeatureState.Pending;
            StateHasChanged();

            await FeatureService.ApplyFeature(selectedProcessType!, featureGroup);

            // Update state to Applied
            featureStates[featureGroup.GroupName] = FeatureState.Applied;
            errorMessage = null;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            featureStates[featureGroup.GroupName] = FeatureState.Available;
            errorMessage = $"Error applying feature: {ex.Message}";
            StateHasChanged();
        }
    }

    private async Task LoadFeatureSettings(AccordionEventArgs e)
    {
        var groupName = e.Name; 
        var group = featureGroups?.FirstOrDefault(g => g.GroupName == groupName);
        if (selectedProcessType == null || string.IsNullOrEmpty(groupName) || group == null)
        {
            ShowMessage(ToastType.Danger, "Invalid process type or feature group.");
            return;
        }
        try
        {
            var groupFeatures = await FeatureService.GetSettingFeaturePrompts(selectedProcessType, group);
            featurePrompts[groupName] = groupFeatures;

        }
        catch (Exception ex) { 
            ShowMessage(ToastType.Danger, $"Error loading features: {ex.Message}");
        }
    }
    

    private void ShowMessage(ToastType toastType, string message) => ToastService.Notify(new ToastMessage(toastType, message));
}
