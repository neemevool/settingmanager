using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Components.SettingsManagement;

public partial class FeatureList
{
    private string? selectedFeatureName;
    private bool isFormVisible = false;
    private string? newSettingName;
    private PositionOption selectedPosition = PositionOption.First; // Default selection
    private string afterSettingName = string.Empty; // Text field value

    [Parameter]
    public IEnumerable<SettingFeature> Features { get; set; } = Enumerable.Empty<SettingFeature>();

    [Parameter]
    public EventCallback<NewSettingArgs> OnApplyFeature { get; set; }

    
    private async Task ApplyFeature(NewSettingArgs args)
    {
        if (OnApplyFeature.HasDelegate)
        {
            await OnApplyFeature.InvokeAsync(args);
        }
    }

    private async Task SubmitFeature(string featureName)
    {
        var args = new NewSettingArgs(featureName, String.Empty, PositionOption.None, String.Empty);
        await ApplyFeature(args);

    }
    private async Task SubmitForm()
    {
        if (string.IsNullOrEmpty(selectedFeatureName))
            return;
        if (string.IsNullOrEmpty(newSettingName))
            return;
        if (selectedPosition == PositionOption.After && string.IsNullOrEmpty(afterSettingName))
            return;

        var args = new NewSettingArgs(selectedFeatureName, newSettingName, selectedPosition, afterSettingName);
        await ApplyFeature(args);
        
        isFormVisible = false;
    }

    private void ShowForm(string featureName)
    {
        selectedFeatureName = featureName;
        isFormVisible = true;
    }

    private void CancelForm()
    {
        selectedFeatureName = null;
        isFormVisible = false;
    }
}
