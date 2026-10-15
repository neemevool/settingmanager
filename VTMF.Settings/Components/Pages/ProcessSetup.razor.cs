using Microsoft.AspNetCore.Components;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Components.Pages;

public partial class ProcessSetup
{
    [Parameter]
    public string ClientName { get; set; } = string.Empty;

    [Parameter]
    public string ProcessTypeName { get; set; } = string.Empty;

    //private ProcessType? processType;
    //private ProcessTypeMetainfo? processTypeMeta;

    protected override async Task OnInitializedAsync()
    {

        //processTypeMeta = await SettingsMetadataService.GetProcessType(ClientName, ProcessTypeName);
    }
}
