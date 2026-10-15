using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface ISettingsMetadataService
{
    Task<IEnumerable<ProcessTypeMetainfo>> GetProcessTypes(string clientName);
    //Task<IEnumerable<SettingFeature>> GetProcessFeatures(ProcessType forProcess, SettingFeatureGroup group);
    Task<IEnumerable<SettingFeatureValue>> GetSettingFeatureValues(ProcessType forProcess, string featureName);

    Task<IEnumerable<SettingFeatureGroup>> GetSettingFeatureGroups(ProcessType forProcess);

    // returns a single SettingValueDescription for the given setting name and value
    ValueTask<SettingValueDescription> GetSettingValueDescription(string settingName, string value);

    ValueTask<SettingDescription> GetSettingDescription(ProcessType forProcess, string settingName);
    Task<(bool success, string message)> UpdateSettingDescription(ProcessType forProcess, SettingDescription settingDescription);
    // returns a list of SettingValuesList for the given setting name
    ValueTask<SettingMeta> GetSettingValuesLists(ProcessType forProcess, string storeTable, string settingName);
}
