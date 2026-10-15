using VTMF.Settings.Entities;
using VTMF.Settings.Models;

namespace VTMF.Settings.Services;

public interface ISettingsService
{

    Task<IEnumerable<ProcessType>> GetProcessTypes(string clientName);
    // returns list of Setting.StoreTable values for the given process type
    Task<ProcessType> GetProcessTypeByName(string clientName, string name);
    Task<ProcessType> GetProcessTypeById(string clientName, int id);
    Task<bool> ProcessTypeNameExists(string clientName, string name, int? excludeId = null);
    Task<int> InsertProcessType(string clientName, ProcessType pt);
    Task UpdateProcessType(string clientName, ProcessType pt);

    Task<(bool success, string message)> EnsureSettingTable(ProcessType forProcess, string storeTable);
    Task<IEnumerable<string>> GetStoreTables(ProcessType forProcess);
    Task<IEnumerable<string>> GetStoreTables(ClientInfo client, TableInfo tableInfo);
    Task<IEnumerable<string>> GetAllStoreTables(ClientInfo client);
    // returns list of current actual Settings (including ListSetting) for the given process type and store table
    Task<IEnumerable<Setting>> GetCurrentSettings(ProcessType forProcess, string storeTable);
    Task ApplyFixes(ProcessType forProcess, string storeTable);
    Task<IEnumerable<Setting>> GetSubsSettings(ProcessType forProcess, SettingTreeNode node);

    // returns list of all available Settings (including ListSetting) for the given process type and store table
    //Task<IEnumerable<Setting>> GetAvailableSettings(ProcessType forProcess, string storeTable);

    // returns a single Feature by name
    ValueTask<Feature> GetFeature(string featureName);

    Task<(bool success, string message)> SaveSettingValue(ProcessType forProcess, string storeTable, string settingName, string newValue);
    Task<(bool success, string message)> SaveSetting(ProcessType forProcess, Setting setting, string newValue);
    Task<(bool success, string message)> DeleteSetting(ProcessType forProcess, Setting setting);
    Task<(bool success, string message)> DeleteNode(ProcessType forProcess, SettingTreeNode node);
    Task<(bool succes, string message)> MoveSettingGroupNodeUp(ProcessType processType, SettingTreeNode node);
    Task<IEnumerable<SettingFeature>> GetSettingFeatures(ProcessType forProcess, SettingTreeNode node);
    Task<IEnumerable<SettingFeatureValue>> GetSettingFeatureValues(ProcessType forProcess, string featureName);

    Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, SettingTreeNode parentSettingNode, NewSettingArgs args);
    Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, SettingTreeNode parentSettingNode, NewChildArgs args);
    Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, TableInfo table, string settingName, string settingValue, string settingType="Setting");

    Task<(bool success, string message)> CopySettings(ProcessType forProcess, ClientInfo sourceClient, string tableName, IProgress<int>? progress = null);

    Task<(bool success, string message)> BackupSettings(ProcessType forProcess, string tableName, string description = "");

    Task<IEnumerable<string>> GetBackupTables(ProcessType forProcess);
    Task<(bool success, string message)> RestoreBackup(ProcessType forProcess, string tableName, string backupTableName);

    Task<string?> GetBackupInfo(ProcessType forProcess, string backupTableName);

    Task<string?> GetCurrentSettingsSession(ProcessType forProcess);
    Task<(bool success, string message)> SetCurrentSettingsSession(ProcessType forProcess, string session);

}
