using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public interface IFeatureService
{
    Task<IEnumerable<FeaturePrompt>> GetSettingFeaturePrompts(ProcessType forProcess, SettingFeatureGroup featureGroup);
    Task<(bool success, string message)> ApplyFeature(ProcessType forProcess, SettingFeatureGroup featureGroup);
}
