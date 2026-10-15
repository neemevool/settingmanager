using Dapper;
using System.Collections;
using System.Linq.Expressions;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class FeatureManagementService(SqlConnectionProvider connectionProvider, ILogger<FeatureManagementService> logger) : IFeatureManagementService
{
    public async Task<IEnumerable<SettingFeatureGroup>> GetFeatureGroups(Expression<Func<SettingFeatureGroup, bool>>? predicate = null)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                select [processtypename], [groupname], [label], [description] from [dbo].[settingsfeaturegroup]
            """;
        if (predicate != null)
        {
            throw new NotImplementedException("Predicate-based filtering is not implemented.");
        }
        var featuregroups = await connection.QueryAsync<SettingFeatureGroup>(sql);
        return featuregroups;
    }
    public async Task<IEnumerable<SettingFeature>> GetFeatures(Expression<Func<SettingFeature, bool>>? predicate = null)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT 
                [processtypename]
                ,[tablename]
                ,[parentsettingname]
                ,[featurename]
                ,[actiontype]
                ,[featuretype]
                ,[description]
                ,[label]
          
            FROM [dbo].[settingsfeature]
            order by [featurename] asc
            """;
        if (predicate != null)
        {
            // Note: Dapper does not support expression trees directly.
            // You would need to convert the expression to SQL manually or use a library that supports it.
            throw new NotImplementedException("Predicate-based filtering is not implemented.");
        }
        var features = await connection.QueryAsync<SettingFeature>(sql);
        return features;
    }

    public async Task<SettingFeature?> GetFeature(string featureName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT 
                [processtypename]
                ,[tablename]
                ,[parentsettingname]
                ,[featurename]
                ,[actiontype]
                ,[featuretype]
                ,[description]
                ,[label]
               
                
            FROM [dbo].[settingsfeature] where [featurename] = @FeatureName
            """;
        var features = await connection.QueryAsync<SettingFeature>(sql, new { FeatureName = featureName });
        return features.FirstOrDefault();
    }

    public async Task<IEnumerable<SettingFeatureValue>> GetFeatureValues(string featureName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT 
                [featurename]
                ,[parentsettingname]
                ,[settingname]
                ,[order]
                ,[settingvalue]
                ,[settingvaluetype]
                ,[promptonapply]
                ,[storetable]
            
            FROM [dbo].[settingsfeaturevalues]
            WHERE [featurename] = @FeatureName order by [order] asc
            """;
        var featureValues = await connection.QueryAsync<SettingFeatureValue>(sql, new { FeatureName = featureName });
        return featureValues;
    }

    public async Task<(bool success, string message)> UpdateFeatureValue(SettingFeatureValue featureValue)
    {
       using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                UPDATE [dbo].[settingsfeaturevalues]
                SET 
                    [settingvalue] = @SettingValue,
                    [promptonapply] = @PromptOnApply
                WHERE 
                    [featurename] = @FeatureName AND 
                    [settingname] = @SettingName
                """;
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            SettingValue = featureValue.SettingValue,
            FeatureName = featureValue.FeatureName,
            SettingName = featureValue.SettingName,
            PromptOnApply = featureValue.PromptOnApply
        });
        if (rowsAffected > 0)
            return (true, "Feature value updated successfully.");
        else
            return (false, "Feature value not found.");
    }

    public async Task<(bool success, string message)> DeleteFeatureGroup(string groupName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                DELETE FROM [dbo].[settingsfeaturegroupfeature]
                WHERE 
                    [groupname] = @groupName
                """;
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            groupName = groupName
        });

        var deleteValuesSql = """
                DELETE FROM [dbo].[settingsfeaturegroup]
                WHERE 
                    [groupname] = @groupName
                """;
        var valueRowsAffected = await connection.ExecuteAsync(deleteValuesSql, new
        {
            groupName = groupName
        });
        if (rowsAffected > 0)
            return (true, "Feature deleted successfully.");
        else
            return (false, "Feature not found.");
    }

    public async Task<(bool success, string message)> DeleteFeature(string featureName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                DELETE FROM [dbo].[settingsfeature]
                WHERE 
                    [featurename] = @FeatureName
                """;
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            FeatureName = featureName
        });

        var deleteValuesSql = """
                DELETE FROM [dbo].[settingsfeaturevalues]
                WHERE 
                    [featurename] = @FeatureName
                """;
        var valueRowsAffected = await connection.ExecuteAsync(deleteValuesSql, new
        {
            FeatureName = featureName
        });
        if (rowsAffected > 0)
            return (true, "Feature deleted successfully.");
        else
            return (false, "Feature not found.");
    }

    public async Task<(bool success, string message)> DeleteFeatureValue(string featureName, string settingName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                DELETE FROM [dbo].[settingsfeaturevalues]
                WHERE 
                    [featurename] = @FeatureName AND 
                    [settingname] = @SettingName
                """;
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            FeatureName = featureName,
            SettingName = settingName
        });
        if (rowsAffected > 0)
            return (true, "Feature value deleted successfully.");
        else
            return (false, "Feature value not found.");
    }

    public async Task<bool> FeatureValueExists(string featureName, string settingName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT COUNT(1)
            FROM [dbo].[settingsfeaturevalues]
            WHERE [featurename] = @FeatureName AND [settingname] = @SettingName
            """;
        var count = await connection.ExecuteScalarAsync<int>(sql, new { FeatureName = featureName, SettingName = settingName });
        return count > 0;
    }

    public async Task<(bool success, string message)> AddNewFeatureValue(SettingFeatureValue featureValue)
    {
        try
        {
            // Check if feature value already exists
            if (await FeatureValueExists(featureValue.FeatureName, featureValue.SettingName))
            {
                return (false, $"A feature value with FeatureName '{featureValue.FeatureName}' and SettingName '{featureValue.SettingName}' already exists.");
            }

            using var connection = connectionProvider.GetDefaultMetaBaseConnection();

            // Check if another feature value has the same order and update their orders
            var existingWithSameOrder = await connection.QueryAsync<SettingFeatureValue>(
                """
                SELECT [featurename]
                      ,[parentsettingname]
                      ,[settingname]
                      ,[order]
                      ,[settingvalue]
                      ,[settingvaluetype]
                      ,[promptonapply]
                      ,[storetable]
                FROM [dbo].[settingsfeaturevalues]
                WHERE [featurename] = @FeatureName AND [order] >= @Order
                ORDER BY [order] DESC
                """,
                new { FeatureName = featureValue.FeatureName, Order = featureValue.Order }
            );

            // Increment order for all existing feature values with same or higher order
            foreach (var existingValue in existingWithSameOrder)
            {
                existingValue.Order += 1;
                await SetFeatureValueOrderNr(existingValue);
            }

            // Insert the new feature value
            var insertSql = """
                INSERT INTO [dbo].[settingsfeaturevalues]
                    ([featurename], [parentsettingname], [settingname], [order], [settingvalue], [settingvaluetype], [promptonapply], [storetable])
                VALUES
                    (@FeatureName, @ParentSettingName, @SettingName, @Order, @SettingValue, @SettingValueType, @PromptOnApply, @StoreTable)
                """;

            await connection.ExecuteAsync(insertSql, new
            {
                FeatureName = featureValue.FeatureName,
                ParentSettingName = featureValue.ParentSettingName,
                SettingName = featureValue.SettingName,
                Order = featureValue.Order,
                SettingValue = featureValue.SettingValue,
                SettingValueType = featureValue.SettingValueType,
                PromptOnApply = featureValue.PromptOnApply,
                StoreTable = featureValue.StoreTable
                
            });

            return (true, "Feature value added successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding new feature value");
            return (false, $"An error occurred: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> SetFeatureValueOrderNr(SettingFeatureValue featureValue)
    {
        try
        {
            using var connection = connectionProvider.GetDefaultMetaBaseConnection();
            var sql = """
                UPDATE [dbo].[settingsfeaturevalues]
                SET [order] = @Order
                WHERE [featurename] = @FeatureName AND [settingname] = @SettingName
                """;

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                Order = featureValue.Order,
                FeatureName = featureValue.FeatureName,
                SettingName = featureValue.SettingName
            });

            if (rowsAffected > 0)
                return (true, "Feature value order updated successfully.");
            else
                return (false, "Feature value not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating feature value order");
            return (false, $"An error occurred: {ex.Message}");
        }
    }

    public async Task<bool> FeatureExists(string featureName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT COUNT(1)
            FROM [dbo].[settingsfeature]
            WHERE [featurename] = @FeatureName
            """;
        var count = await connection.ExecuteScalarAsync<int>(sql, new { FeatureName = featureName });
        return count > 0;
    }

    public async Task<bool> FeatureGroupExists(string groupName)
    {
        using var connection = connectionProvider.GetDefaultMetaBaseConnection();
        var sql = """
                SELECT COUNT(1)
            FROM [dbo].[settingsfeaturegroup]
            WHERE [groupname] = @groupName
            """;
        var count = await connection.ExecuteScalarAsync<int>(sql, new { groupName });
        return count > 0;
    }

    public async Task<(bool success, string message)> AddNewFeatureGroup(SettingFeatureGroup featureGroup)
    {
        try
        {
            // Check if feature group already exists
            if (await FeatureGroupExists(featureGroup.GroupName))
            {
                return (false, $"A feature group with GroupName '{featureGroup.GroupName}' already exists.");
            }
            using var connection = connectionProvider.GetDefaultMetaBaseConnection();
            var insertSql = """
                INSERT INTO [dbo].[settingsfeaturegroup]
                    ([processtypename], [groupname], [label], [description])
                VALUES
                    (@ProcessTypeName, @GroupName, @Label, @Description)
                """;
            await connection.ExecuteAsync(insertSql, new
            {
                ProcessTypeName = featureGroup.ProcessTypeName,
                GroupName = featureGroup.GroupName,
                Label = featureGroup.Label,
                Description = featureGroup.Description
            });
            return (true, "Feature group added successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding new feature group");
            return (false, $"An error occurred: {ex.Message}");
        }
    }
    public async Task<(bool success, string message)> AddNewFeature(SettingFeature feature)
    {
        try
        {
            // Check if feature already exists
            if (await FeatureExists(feature.FeatureName))
            {
                return (false, $"A feature with FeatureName '{feature.FeatureName}' already exists.");
            }

            using var connection = connectionProvider.GetDefaultMetaBaseConnection();
            var insertSql = """
                INSERT INTO [dbo].[settingsfeature]
                    ([processtypename], [tablename], [parentsettingname], [featurename], [actiontype], [featuretype], [description], [label])
                VALUES
                    (@ProcessTypeName, @TableName, @ParentSettingName, @FeatureName, @ActionType, @FeatureType, @Description, @Label)
                """;

            await connection.ExecuteAsync(insertSql, new
            {
                ProcessTypeName = feature.ProcessTypeName,
                TableName = feature.TableName,
                ParentSettingName = feature.ParentSettingName,
                FeatureName = feature.FeatureName,
                ActionType = feature.ActionType,
                FeatureType = feature.FeatureType,
                Description = feature.Description,
                Label = feature.Label
            });

            return (true, "Feature added successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding new feature");
            return (false, $"An error occurred: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> UpdateFeature(SettingFeature feature)
    {
        try
        {
            using var connection = connectionProvider.GetDefaultMetaBaseConnection();

            // Check if feature exists
            if (!await FeatureExists(feature.FeatureName))
            {
                return (false, $"Feature with FeatureName '{feature.FeatureName}' not found.");
            }

            var updateSql = """
                UPDATE [dbo].[settingsfeature]
                SET [processtypename] = @ProcessTypeName,
                    [tablename] = @TableName,
                    [parentsettingname] = @ParentSettingName,
                    [actiontype] = @ActionType,
                    [featuretype] = @FeatureType,
                    [description] = @Description,
                    [label] = @Label
                WHERE [featurename] = @FeatureName
                """;

            var rowsAffected = await connection.ExecuteAsync(updateSql, new
            {
                ProcessTypeName = feature.ProcessTypeName,
                TableName = feature.TableName,
                ParentSettingName = feature.ParentSettingName,
                FeatureName = feature.FeatureName,
                ActionType = feature.ActionType,
                FeatureType = feature.FeatureType,
                Description = feature.Description,
                Label = feature.Label
            });

            if (rowsAffected > 0)
                return (true, "Feature updated successfully.");
            else
                return (false, "Feature could not be updated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating feature");
            return (false, $"An error occurred: {ex.Message}");
        }
    }
}
