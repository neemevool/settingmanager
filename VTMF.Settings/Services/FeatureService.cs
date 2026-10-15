using Dapper;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class FeatureService(SqlConnectionProvider connectionProvider) : IFeatureService
{
    public async Task<IEnumerable<FeaturePrompt>> GetSettingFeaturePrompts(ProcessType forProcess, SettingFeatureGroup featureGroup)
    {
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        
        var sql = $"""
                select 
                    sf.tablename, fv.settingname,fv.settingvalue,fv.promptonapply 
                    from settingsfeaturevalues fv 
                    join settingsfeature sf on sf.featurename=fv.featurename 
                    join settingsfeaturegroupfeature fg on fg.featurename=sf.featurename
                    where fg.groupname = @FeatureGroup
            """;

        Task<IEnumerable<FeaturePrompt>>? featureValues = connection.QueryAsync<FeaturePrompt>(sql, new { FeatureGroup = featureGroup.GroupName });
        return await featureValues;
    }

    public async Task<(bool success, string message)> ApplyFeature(ProcessType forProcess, SettingFeatureGroup featureGroup)
    {
        //var features = await metadataService.GetProcessFeatures(forProcess, featureGroup);
        //if (!features.Any())
        //{
        //    logger.LogWarning("No features found for ProcessType {ProcessType} and FeatureGroup {FeatureGroup}", forProcess.ProcessTypeName, featureGroup.GroupName);
        //    return (false, "No features found to apply.");
        //}
        //foreach (var feature in features)
        //{
        //    var featureValues = await metadataService.GetSettingFeatureValues(forProcess, feature.FeatureName);
        //    foreach (var featureValue in featureValues)
        //    {
        //        var configuredValue = featureValue.Configure(forProcess);
        //        var result = await SaveSetting(forProcess.ClientInfo, feature.TableInfo, configuredValue);
        //    }
        //}

        return (true, "Features applied successfully.");
    }

    private async Task<(bool success, string message)> SaveSetting(ClientInfo client, TableInfo table, SettingFeatureValue featureValue)
    {
        if (table.IsSettingsaTable)
        {
            // Save to settings table
            return await SaveToSettingsTable(client, featureValue);
        }
        else
        {
            // Save to other table
            return (true, "Feature saved successfully.");
        }
    
    }

    private async Task<(bool success, string message)> SaveToSettingsTable(ClientInfo client, SettingFeatureValue featureValue)
    {
        var settingExists = await SettingExists(client, featureValue.SettingName);
        if (settingExists)
        {
            var updateSql = "UPDATE [dbo].[settings] SET [VALUE] = @Value WHERE [NAME] = @Name";
            using var connection = connectionProvider.GetConnection(client.ClientName);
            try
            {
                await connection.ExecuteAsync(updateSql, new
                {
                    Name = featureValue.SettingName,
                    Value = featureValue.SettingValue
                });
            }
            catch (Exception ex)
            {
                return (false, $"Error updating database: {ex.Message}");
            }
        }
        else
        {
            var insertSql = "INSERT INTO [dbo].[settings] ([ID],[NAME],[VALUE]) VALUES ((NEXT VALUE FOR [dbo].[settings_seq]), @Name, @Value)";
            using var connection = connectionProvider.GetConnection(client.ClientName);
            try
            {
                await connection.ExecuteAsync(insertSql, new
                {
                    Name = featureValue.SettingName,
                    Value = featureValue.SettingValue
                });
            }
            catch (Exception ex)
            {
                return (false, $"Error inserting into database: {ex.Message}");
            }
        }
        

        return (true, "Setting saved successfully.");

    }

    private async Task<bool> SettingExists(ClientInfo client, string settingName)
    {
        var sql = "SELECT COUNT(1) FROM [dbo].[settings] WHERE [NAME] = @Name";
        using var connection = connectionProvider.GetConnection(client.ClientName);
        var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = settingName });
        return count > 0;
    }


}
