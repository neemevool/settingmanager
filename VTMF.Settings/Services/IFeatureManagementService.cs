using System.Linq.Expressions;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface IFeatureManagementService
{
    Task<IEnumerable<SettingFeatureGroup>> GetFeatureGroups(Expression<Func<SettingFeatureGroup, bool>>? predicate = null);
    Task<SettingFeature?> GetFeature(string featureName);
    Task<IEnumerable<SettingFeature>> GetFeatures(Expression<Func<SettingFeature, bool>>? predicate = null);
    Task<IEnumerable<SettingFeatureValue>> GetFeatureValues(string featureName);
    Task<bool> FeatureValueExists(string featureName, string settingName);
    
    Task<(bool success, string message)> SetFeatureValueOrderNr(SettingFeatureValue featureValue);
    Task<bool> FeatureExists(string featureName);
    Task<(bool success, string message)> AddNewFeature(SettingFeature feature);
    Task<(bool success, string message)> AddNewFeatureValue(SettingFeatureValue featureValue);
    Task<(bool success, string message)> AddNewFeatureGroup(SettingFeatureGroup featureGroup);
    Task<(bool success, string message)> UpdateFeature(SettingFeature feature);
    Task<(bool success, string message)> UpdateFeatureValue(SettingFeatureValue featureValue);
    Task<(bool success, string message)> DeleteFeatureValue(string featureName, string settingName);
    Task<(bool success, string message)> DeleteFeature(string featureName);
    Task<(bool success, string message)> DeleteFeatureGroup(string groupName);
    Task<bool> FeatureGroupExists(string groupName);
}
