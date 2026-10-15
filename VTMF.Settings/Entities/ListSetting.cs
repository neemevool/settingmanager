namespace VTMF.Settings.Entities;


public class ListSetting : Setting
{
    #region Fields

    private List<Setting> slist = new List<Setting>();
    private Dictionary<string, Setting> settingValues = new Dictionary<string, Setting>();
    #endregion Fields

    #region Constructors


    public ListSetting()
        : base()
    {
    }

    #endregion Constructors

    #region Properties


    public int SettingsCount
    {
        get
        {
            return slist != null ? slist.Count : 0;
        }
    }


    public List<Setting> Settings
    {
        get
        {
            return slist;
        }
    }



    #endregion Properties

    public void AddSetting(Setting setting)
    {
        if (setting != null)
        {
            slist.Add(setting);
            settingValues[setting.Name] = setting;
        }
    }
}