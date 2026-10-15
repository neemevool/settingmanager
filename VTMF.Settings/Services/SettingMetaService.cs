using Dapper;
using System.Reflection.Metadata;
using VTMF.Settings.Entities;
using VTMF.Settings.Models;

namespace VTMF.Settings.Services;

public class SettingMetaService(SqlConnectionProvider connectionProvider) : ISettingsMetadataService
{
    public async Task<IEnumerable<ProcessTypeMetainfo>> GetProcessTypes(string clientName)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetMetaBaseConnection(sanitizedClientName);
        
        var sql = $"""
                SELECT 
                    [processtypename] as [Name],
                    [processtype] as [Type],
                    [description] as [Description],
                    [label] as [Label]
                FROM [dbo].[settingsprocesstypes]
            """;
        var processTypes = connection.QueryAsync<ProcessTypeMetainfo>(sql);

        return await processTypes;
    }

    public async Task<IEnumerable<SettingFeatureGroup>> GetSettingFeatureGroups(ProcessType forProcess)
    {
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        
        var sql = $"""
                SELECT 
                    [processtypename] as ProcessTypeName,
                    [groupname] as GroupName,
                    [label] as Label,
                    [description]
                FROM [dbo].[settingsfeaturegroup] 
                WHERE [processtypename] = @ProcessTypeName
            """;
                
        var featureGroups = connection.QueryAsync<SettingFeatureGroup>(sql, new { forProcess.ProcessTypeName });
        return await featureGroups;
    }

    //public async Task<IEnumerable<SettingFeature>> GetProcessFeatures(ProcessType forProcess, SettingFeatureGroup group)
    //{
    //    using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        
    //    var sql = $"""
    //            SELECT 
    //                [featuregroup],
    //                [processtypename] as ProcessTypeName,
    //                [parentsettingname] as ParentSettingName,
    //                [featurename] as FeatureName,
    //                [actiontype],
    //                [featuretype],
    //                [description],
    //                [tablename],
    //                [label]
    //            FROM [dbo].[settingsfeature] 
    //            WHERE [processtypename] = @ProcessTypeName and [actiontype]='process' and [featuregroup] = @FeatureGroup
    //        """;

    //    var features = connection.QueryAsync<SettingFeature>(sql, new { forProcess.ProcessTypeName, FeatureGroup = group.GroupName });
    //    return await features;
    //}

    public async Task<IEnumerable<SettingFeatureValue>> GetSettingFeatureValues(ProcessType forProcess, string featureName)
    {
        ArgumentNullException.ThrowIfNull(featureName);
        
        var sql = """
            SELECT 
                [featurename] as FeatureName,
                [parentsettingname] as ParentSettingName,
                [settingname] as SettingName,
                [order] as [Order],
                [settingvalue] as SettingValue,
                [settingvaluetype] as SettingValueType,
                [promptonapply] as PromptOnApply
            FROM [dbo].[settingsfeaturevalues] 
            WHERE [featurename]=@featureName
            """;
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var featureValues = connection.QueryAsync<SettingFeatureValue>(sql,
            new { featureName });
        return await featureValues;
    }

    public async ValueTask<SettingDescription> GetSettingDescription(ProcessType forProcess, string settingName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        
        var sql = $"SELECT [settingname],[description] FROM [dbo].[settingsmeta] where [processtypename] = @processTypeName and [settingname]=@settingName";
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var existing = await connection.QuerySingleOrDefaultAsync<SettingDescription>(sql,
            new { processTypeName = forProcess.ProcessTypeName, settingName });
        //var fake = new SettingDescription()
        //{
        //    SettingName = settingName,
        //    Description = $"Desc: {settingName}, table: {storeTable}, proce: {forProcess.ProcessTypeName}"
        //};
        //return fake;
        
        return existing ?? new SettingDescription(settingName, String.Empty);

    }

    

    public async Task<(bool success, string message)> UpdateSettingDescription(ProcessType forProcess, SettingDescription settingDescription)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(settingDescription);
        
        var sql = $"""
            MERGE INTO [dbo].[settingsmeta] AS target
            USING (SELECT @processTypeName AS processtypename, @settingName AS settingname) AS source
            ON target.[processtypename] = source.processtypename AND target.[settingname] = source.settingname
            WHEN MATCHED THEN
                UPDATE SET [description] = @description
            WHEN NOT MATCHED THEN
                INSERT ([processtypename], [settingname], [description])
                VALUES (@processTypeName, @settingName, @description);
            """;
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var result = await connection.ExecuteAsync(sql,
            new
            {
                description = settingDescription.Description,
                processTypeName = forProcess.ProcessTypeName,
                settingName = settingDescription.SettingName
                
            });
        if (result > 0)
        {
            return (true, "Setting saved successfully");
        }
        else
        {
            return (false, "No operation performed");
        }
    }

    public ValueTask<SettingValueDescription> GetSettingValueDescription(string settingName, string value)
    {
        throw new NotImplementedException();
    }

    public async ValueTask<SettingMeta> GetSettingValuesLists(ProcessType forProcess, string storeTable, string settingName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(storeTable);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var settingNameParts = settingName.Split('.');
        var metakey = SettingTreeNode.GetMetaKey(settingName);
        var sql = $"SELECT [settingvaluetype],[valueslist] FROM [dbo].[settingsmeta] where [processtypename] = @processTypeName and [settingname]=@metakey";
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var settingMetaResult = await connection.QuerySingleOrDefaultAsync<(int valueType,string valuesList)>(sql,
            new { processTypeName = forProcess.ProcessTypeName, metakey });
        
        var result = new SettingMeta() { SettingName = settingName };
        if (settingMetaResult != default)
        {
            var list = settingMetaResult.valuesList.Split(",");
            result.Values = list.ToList();
            result.SettingValueType = (SettingValueType)settingMetaResult.valueType;
        }
        else
        {
            _ = await EnsureEmptySettingMetaExists(forProcess, metakey);
            result.Values = new List<string>();
            result.SettingValueType = SettingValueType.String;
        }
        return result;
    }

    private async Task<bool> EnsureEmptySettingMetaExists(ProcessType forProcess, string metaKey)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        
        var sql = $"""
            if not exists (select 1 from [dbo].[settingsmeta] where [processtypename]=@processTypeName and [settingname]=@metaKey)
            begin
                INSERT INTO [dbo].[settingsmeta] 
                    ([processtypename], [settingname]) 
                VALUES 
                    (@processTypeName, @metaKey);
            end
            """;
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var result = await connection.ExecuteAsync(sql,
            new
            {
                processTypeName = forProcess.ProcessTypeName,
                metaKey
            });
        return result > 0;
    }
}
