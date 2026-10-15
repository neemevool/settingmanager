namespace VTMF.Settings.Entities;

public class SettingFeatureGroup
{
    //[processtypename][nvarchar] (50) NOT NULL,
    public string ProcessTypeName { get; set; } = null!;

    //[groupname] [nvarchar] (50) NOT NULL,
    public string GroupName { get; set; } = null!;

    public string Label { get; set; } = null!;
    //[description] [nvarchar] (4000) NULL
    public string Description { get; set; } = null!;
}
