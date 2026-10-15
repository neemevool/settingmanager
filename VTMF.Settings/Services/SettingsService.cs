using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Text.RegularExpressions;
using VTMF.Settings.Components.Pages;
using VTMF.Settings.Components.SettingsManagement;
using VTMF.Settings.Entities;
using VTMF.Settings.Models;

namespace VTMF.Settings.Services;

public class SettingsService(SqlConnectionProvider connectionProvider) : ISettingsService
{
    public const string ServerSettingsTableName = "ServerProcessSettings";
    public const string SettingsLogTableName = "settingslog";
    public const string SettingSessionTableName = "settingssession";
    private readonly static string GroupTypeColumn = "Column";
    private readonly static string GroupTypeGroup = "Group";

    //public async Task<IEnumerable<Setting>> GetAvailableSettings(ProcessType forProcess, string storeTable)
    //{
    //    ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
    //    ArgumentNullException.ThrowIfNull(storeTable);
    //    var sanitizedTableName = ProcessType.SanitizeName(storeTable);
    //    var sql = $"SELECT [settingname] as NAME,[allowedvalues] as VALUE,'Setting' as SETTINGTYPE,[FEATURENAME],[VALUETYPE],[settingtablename] as STORETABLE FROM [dbo].[settingsmeta] where [processtypename] = @processTypeName and [settingtablename]=@storeTable";
    //    using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
    //    var settings = connection.QueryAsync<Setting>(sql, 
    //        new { processTypeName = forProcess.ProcessTypeName, storeTable });
    //    return await settings;
    //}

    public async Task<IEnumerable<SettingFeature>> GetSettingFeatures(ProcessType forProcess, SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);

        var tablename = ProcessType.SanitizeName(node.StoreTable).Replace(forProcess.Name, "");
        var sql = """
            SELECT 
                
                [processtypename] as ProcessTypeName,
                [parentsettingname] as ParentSettingName,
                [featurename] as FeatureName,
                [actiontype],
                [featuretype],
                [description],
                [tablename],
                [label]

            FROM [dbo].[settingsfeature] 
            WHERE [processtypename] = @processTypeName 
            AND [tablename] = @tablename 
            AND (
                [parentsettingname] = @parentsettingname 
                OR [parentsettingname] = @wildcardsettingname
            )
            """;
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var features = await connection.QueryAsync<SettingFeature>(sql,
            new { processTypeName = forProcess.ProcessTypeName, tablename, parentsettingname = node.FullPath, wildcardsettingname = node.GetWildCardPath() });

        return features;
    }


    public async Task<SettingFeature> GetSettingFeature(ProcessType forProcess, SettingTreeNode parentSetting, string featureName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(parentSetting);
        var tablename = parentSetting.StoreTable.Replace(forProcess.Name, "");
        var sql = """
            SELECT 
                [processtypename] as ProcessTypeName,
                [parentsettingname] as ParentSettingName,
                [featurename] as FeatureName,
                [actiontype],
                [featuretype],
                [description],
                [tablename],
                [label]
            FROM [dbo].[settingsfeature] 
            WHERE [processtypename] = @processTypeName and [tablename]=@tablename and [featurename]=@featureName;
            """;
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        return await connection.QuerySingleAsync<SettingFeature>(sql,
            new { processTypeName = forProcess.ProcessTypeName, tablename, featureName });

    }

    public async Task<IEnumerable<SettingFeatureValue>> GetSettingFeatureValues(ProcessType forProcess, string featureName)
    {
        ArgumentNullException.ThrowIfNull(featureName);
        
        var sql = $"SELECT [featurename] as FeatureName,[parentsettingname] as ParentSettingName,[settingname] as SettingName,[order] as [Order],[settingvalue] as SettingValue,[settingvaluetype],[promptonapply],[storetable] FROM [dbo].[settingsfeaturevalues] where [featurename]=@featureName order by [order] asc";
        using var connection = connectionProvider.GetMetaBaseConnection(forProcess.ClientName);
        var featureValues = connection.QueryAsync<SettingFeatureValue>(sql,
            new { featureName });
        return await featureValues;
    }

    public async Task ApplyFixes(ProcessType forProcess, string storeTable)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        if (storeTable.Contains("Document"))
        {
            var sql = $@"UPDATE [dbo].[{storeTable}] SET [settingtype]='SettingGroup' where [NAME] like 'Detail.%' and [NAME] not like 'Detail.%.%' and [settingtype]='Setting'";
            await connection.ExecuteAsync(sql);
        }
    }

    public async Task<IEnumerable<Setting>> GetCurrentSettings(ProcessType forProcess, string storeTable)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(storeTable);

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        if (storeTable == ServerSettingsTableName)
        {
            storeTable = "ServerProcessSettings";
            var serverSettingsSql = $@"
                SELECT REPLACE(s.[NAME],@processname+'.','') as [NAME],s.[VALUE],s.[SETTINGTYPE],s.[LISTVALUE],s.[ORDERNR],null as FEATURENAME,null as [VALUETYPE],'ServerProcessSettings' as STORETABLE
                FROM[dbo].[ServerProcessSettings] s where[NAME] like @processname+'%' order by s.[ORDERNR] asc";
            var settings = connection.QueryAsync<Setting>(serverSettingsSql, new { processname = forProcess.Name });
            var allSettings = await settings;
            return allSettings.Where(s => s.Name != "Updated");
        }
        else
        {
            var sanitizedTableName = ProcessType.SanitizeName(storeTable);
            var processSettingsSql = $"""
                    SELECT s.[NAME],s.[VALUE],s.[SETTINGTYPE],s.[LISTVALUE],s.[ORDERNR],null as FEATURENAME,null as [VALUETYPE],'{sanitizedTableName}' as STORETABLE FROM [dbo].[{sanitizedTableName}] s 
                    where s.[WORKERCODE]='ALL'
                    order by s.[ORDERNR] asc
                    """;
            var settings = connection.QueryAsync<Setting>(processSettingsSql);
            var allSettings = await settings;
            return allSettings.Where(s => s.Name != "Updated");
        }

    }

    public async Task<IEnumerable<Setting>> GetSubsSettings(ProcessType forProcess, SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(node.StoreTable);
        var sanitizedTableName = ProcessType.SanitizeName(node.StoreTable);
        // For a [NAME] keyed group (Detail style) the group row itself carries the node path, so it
        // is not covered by the members prefix, but it still belongs to this branch of the tree.
        var groupRowCondition = SettingTreeNode.IsNameKeyedGroup(node.FullPath)
            ? " or ([SETTINGTYPE]='SettingGroup' and [NAME] = @groupname)"
            : string.Empty;
        var sql = $"""
                SELECT s.[NAME],s.[VALUE],s.[SETTINGTYPE],s.[LISTVALUE],s.[ORDERNR],null as FEATURENAME,null as [VALUETYPE],'{sanitizedTableName}' as STORETABLE FROM [dbo].[{sanitizedTableName}] s
                where ([NAME] like @parentname{groupRowCondition}) and workercode='ALL' order by s.[ORDERNR] asc
            """;
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var searchPrefix = node.FullPath + ".%";
        var groupName = node.FullPath ?? string.Empty;
        if (sanitizedTableName == "ServerProcessSettings")
        {
            searchPrefix = Setting.AppendTablePrefix(searchPrefix, forProcess.Name);
            groupName = Setting.AppendTablePrefix(groupName, forProcess.Name);
        }
        var settings = connection.QueryAsync<Setting>(sql, new { parentname = searchPrefix, groupname = groupName });
        var allSettings = await settings;
        if (sanitizedTableName == "ServerProcessSettings")
        {
            foreach (var setting in allSettings)
            {
                setting.Name = Setting.RemoveTablePrefix(setting.Name, forProcess.Name);
            }
        }
        return allSettings.Where(s => s.Name != "Updated");
    }

    public ValueTask<Feature> GetFeature(string featureName)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<ProcessType>> GetProcessTypes(string clientName)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var processTypes = connection.QueryAsync<ProcessType>($"SELECT [ID],[NAME],[DISPLAYLABEL],[TAG],[PROCESSTYPE] as ProcessTypeName,[DATAPROCEDURES],[SETTINGPROCEDURES],[ENVIRONMENT],[AUTOSTART],[SCREEN],[ORDERNR],[DEFAULTVISIBLE],[WORKERGROUPS],'{clientName}' as [CLIENTNAME]  FROM [dbo].[processtypes]");
        return await processTypes;
    }

    public async Task<ProcessType> GetProcessTypeByName(string clientName, string name)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var processType = connection.QuerySingleAsync<ProcessType>($"SELECT [ID],[NAME],[DISPLAYLABEL],[TAG],[PROCESSTYPE] as ProcessTypeName,[DATAPROCEDURES],[SETTINGPROCEDURES],[ENVIRONMENT],[AUTOSTART],[SCREEN],[ORDERNR],[DEFAULTVISIBLE],[WORKERGROUPS],'{clientName}' as [CLIENTNAME]  FROM [dbo].[processtypes] where [NAME]=@name"
            , new { name });
        return await processType;
    }
    public async Task<ProcessType> GetProcessTypeById(string clientName, int id)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var processType = connection.QuerySingleAsync<ProcessType>($"SELECT [ID],[NAME],[DISPLAYLABEL],[TAG],[PROCESSTYPE] as ProcessTypeName,[DATAPROCEDURES],[SETTINGPROCEDURES],[ENVIRONMENT],[AUTOSTART],[SCREEN],[ORDERNR],[DEFAULTVISIBLE],[WORKERGROUPS],'{clientName}' as [CLIENTNAME]  FROM [dbo].[processtypes] where [ID]=@id"
            , new { id });
        return await processType;
    }

    public async Task<bool> ProcessTypeNameExists(string clientName, string name, int? excludeId = null)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var sql = @"SELECT CASE WHEN EXISTS (SELECT 1 FROM [dbo].[processtypes] WHERE LOWER([NAME]) = LOWER(@name) AND (@excludeId IS NULL OR [ID] <> @excludeId)) THEN 1 ELSE 0 END";
        var result = await connection.QuerySingleAsync<int>(sql, new { name, excludeId });
        return result == 1;
    }

    public async Task<int> InsertProcessType(string clientName, ProcessType pt)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var sql = @"
            INSERT INTO [dbo].[processtypes] ([NAME],[DISPLAYLABEL],[TAG],[PROCESSTYPE],[DATAPROCEDURES],[SETTINGPROCEDURES],[ENVIRONMENT],[AUTOSTART],[SCREEN],[ORDERNR],[DEFAULTVISIBLE],[WORKERGROUPS])
            OUTPUT INSERTED.[ID]
            VALUES (@name, @displayLabel, @tag, @processTypeName, @dataProcedures, @settingProcedures, @environment, @autoStart, @screen, @orderNr, @defaultVisible, @workerGroups)
        ";
        var newId = await connection.QuerySingleAsync<int>(sql, new
        {
            name = pt.Name,
            displayLabel = pt.DisplayLabel,
            tag = pt.Tag,
            processTypeName = pt.ProcessTypeName,
            dataProcedures = pt.DataProcedures,
            settingProcedures = pt.SettingProcedures,
            environment = pt.Environment,
            autoStart = pt.AutoStart,
            screen = pt.Screen,
            orderNr = pt.OrderNr,
            defaultVisible = pt.DefaultVisible,
            workerGroups = pt.WorkerGroups
        });
        return newId;
    }

    public async Task UpdateProcessType(string clientName, ProcessType pt)
    {
        var sanitizedClientName = ProcessType.SanitizeName(clientName);
        using var connection = connectionProvider.GetConnection(sanitizedClientName);
        var sql = @"
            UPDATE [dbo].[processtypes]
            SET [DISPLAYLABEL] = @displayLabel,
                [TAG] = @tag,
                [DATAPROCEDURES] = @dataProcedures,
                [SETTINGPROCEDURES] = @settingProcedures,
                [ENVIRONMENT] = @environment,
                [AUTOSTART] = @autoStart,
                [SCREEN] = @screen,
                [ORDERNR] = @orderNr,
                [DEFAULTVISIBLE] = @defaultVisible,
                [WORKERGROUPS] = @workerGroups
            WHERE [ID] = @id
        ";
        await connection.ExecuteAsync(sql, new
        {
            id = pt.Id,
            displayLabel = pt.DisplayLabel,
            tag = pt.Tag,
            dataProcedures = pt.DataProcedures,
            settingProcedures = pt.SettingProcedures,
            environment = pt.Environment,
            autoStart = pt.AutoStart,
            screen = pt.Screen,
            orderNr = pt.OrderNr,
            defaultVisible = pt.DefaultVisible,
            workerGroups = pt.WorkerGroups
        });
    }

    public async Task<IEnumerable<string>> GetStoreTables(ProcessType forProcess)
    {
        var sql = $@"
        select t.TABLE_NAME from INFORMATION_SCHEMA.TABLES t 
            cross apply (select top 1 * from INFORMATION_SCHEMA.COLUMNS c where c.TABLE_NAME = t.TABLE_NAME and c.COLUMN_NAME='settingtype') as cols 
            where t.TABLE_NAME like @processTypeName
            union all select 'ServerProcessSettings'
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var storeTables = await connection.QueryAsync<string>(sql, new { processTypeName = $"{forProcess.Name}%" });
        var storeTablesList = storeTables.Where(t => t.Equals("ServerProcessSettings")  ||  IsUppercase(t.Replace(forProcess.Name, "").First())).ToList();
        return storeTablesList;
        bool IsUppercase(char c) => c >= 'A' && c <= 'Z';
    }

    public async Task<IEnumerable<string>> GetStoreTables(ClientInfo client, TableInfo tableInfo)
    {
        var sql = $@"
        select t.TABLE_NAME from INFORMATION_SCHEMA.TABLES t 
            cross apply (select top 1 * from INFORMATION_SCHEMA.COLUMNS c where c.TABLE_NAME = t.TABLE_NAME and c.COLUMN_NAME='settingtype') as cols 
            where t.TABLE_NAME like @tableName
            union all select 'ServerProcessSettings'
        ";
        using var connection = connectionProvider.GetConnection(client.ClientName);
        var storeTables = await connection.QueryAsync<string>(sql, new { tableName = $"%{tableInfo.TableName}" });
        return storeTables;
    }

    public async Task<IEnumerable<string>> GetAllStoreTables(ClientInfo client)
    {
        var sql = $@"
        select t.TABLE_NAME from INFORMATION_SCHEMA.TABLES t 
            cross apply (select top 1 * from INFORMATION_SCHEMA.COLUMNS c where c.TABLE_NAME = t.TABLE_NAME and c.COLUMN_NAME='settingtype') as cols 
            where t.TABLE_NAME not like 'bck_%' and t.TABLE_NAME not in ('settings','xposettings','schedulerSettings','ServerProcessSettings')
        ";
        using var connection = connectionProvider.GetConnection(client.ClientName);
        var storeTables = await connection.QueryAsync<string>(sql);
        return storeTables;
    }

    public async Task<(bool success, string message)> SaveSettingValue(ProcessType forProcess, string storeTable, string settingName, string newValue)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(storeTable);
        ArgumentNullException.ThrowIfNull(settingName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        if (sanitizedTableName == "ServerProcessSettings")
        {
            settingName = Setting.AppendTablePrefix(settingName, forProcess.Name);
        }
            var sql = $@"
            UPDATE [dbo].[{sanitizedTableName}] 
            SET [VALUE] = @newValue 
            WHERE [NAME] = @settingName and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                settingName,
                newValue
            });
            if (rowsAffected == 0)
            {
                return (false, "Setting not found.");
            }
            await LogSettingChange(forProcess, sanitizedTableName, settingName, newValue);
        }
        catch (Exception ex)
        {
            return (false, $"Error saving to database: {ex.Message}");
        }
        return (true, "Setting saved successfully.");
    }

    public async Task<(bool success, string message)> SaveSetting(ProcessType forProcess, Setting setting, string newValue)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(setting.StoreTable);

        var sanitizedTableName = ProcessType.SanitizeName(setting.StoreTable);
        if (sanitizedTableName == "ServerProcessSettings")
        {
            setting.AppendTableName(forProcess.Name);

        }
        var sql = $@"
        MERGE [dbo].[{sanitizedTableName}] AS target
        USING (SELECT @Name AS Name, @Value AS Value, @SettingType AS SettingType, @ListValue AS ListValue, @OrderNr AS OrderNr) AS source
        ON target.[NAME] = source.Name and target.workercode='ALL'
        WHEN MATCHED THEN
            UPDATE SET 
                [VALUE] = source.Value,
                [SETTINGTYPE] = source.SettingType,
                [LISTVALUE] = source.ListValue,
                [ORDERNR] = source.OrderNr
        WHEN NOT MATCHED THEN
            INSERT ([NAME], [VALUE], [SETTINGTYPE], [LISTVALUE], [ORDERNR],[WORKERCODE])
            VALUES (source.Name, source.Value, source.SettingType, source.ListValue, source.OrderNr,'ALL');";

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql, new
            {
                setting.Name,
                Value = newValue,
                setting.SettingType,
                setting.ListValue,
                setting.OrderNr
            });
            await LogSettingChange(forProcess, sanitizedTableName, setting.Name, newValue, setting.SettingType ?? "Setting");
        }
        catch (Exception ex)
        {
            return (false, $"Error saving to database: {ex.Message}");
        }

        return (true, "Setting saved successfully.");
    }

    private async Task<(bool success, string message)> InsertSetting(ProcessType forProcess, Setting setting)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(setting.StoreTable);
        var sanitizedTableName = ProcessType.SanitizeName(setting.StoreTable);
        var newSettingName = setting.Name;
        if (setting.StoreTable == ServerSettingsTableName)
        {
            newSettingName = Setting.AppendTablePrefix(newSettingName, forProcess.Name);
        }
        var sql = $@"INSERT INTO [dbo].[{sanitizedTableName}] ([NAME],[VALUE],[SETTINGTYPE],[LISTVALUE],[WORKERCODE],[ORDERNR]) VALUES (@Name,coalesce(@Value,''),@SettingType,@ListValue,'ALL',@OrderNr)";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql, new
            {
                Name = newSettingName,
                setting.Value,
                setting.SettingType,
                setting.ListValue,
                setting.OrderNr
            });
            await LogSettingChange(forProcess, sanitizedTableName, newSettingName, setting.Value, setting.SettingType ?? "Setting");
        }
        catch (Exception ex)
        {
            return (false, $"Error inserting to database: {ex.Message}");
        }
        return (true, "Setting inserted successfully.");
    }

    private async Task<(bool success, string message)> UpdateOrderNr(ProcessType forProcess, string tableName, int fromNr, int amount)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        await EnsureOrdernumbersSequential(forProcess, tableName);
        string sanitizedTableName = ProcessType.SanitizeName(tableName);

        var sql = $@"UPDATE [dbo].[{sanitizedTableName}] SET [ORDERNR] = [ORDERNR] + @amount WHERE [ORDERNR] >= @fromNr and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql, new { amount, fromNr });
            return (true, "Order numbers updated successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"Error updating order numbers: {ex.Message}");
        }
    }

    private async Task EnsureOrdernumbersSequential(ProcessType forProcess, string tableName)
    {
        var numbersOk = await AreOrderNumbersCorrect(forProcess, tableName);
        if (numbersOk) return;
        await RenumberOrderNumbers(forProcess, tableName);
    }

    private async Task<bool> AreOrderNumbersCorrect(ProcessType forProcess, string tableName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(tableName);
        string sanitizedTableName = ProcessType.SanitizeName(tableName);
        var sql = $@"SELECT COUNT(*) FROM (SELECT COUNT(*) AS cnt FROM [dbo].[{sanitizedTableName}] where workercode='ALL' GROUP BY [ORDERNR]) c WHERE c.cnt > 1";

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.ExecuteScalarAsync<int>(sql);
        return count == 0;
    }

    private async Task RenumberOrderNumbers(ProcessType forProcess, string tableName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(tableName);
        string sanitizedTableName = ProcessType.SanitizeName(tableName);

        var sql = $@"
            WITH CTE AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR], [NAME]) AS NewOrderNr
                FROM [dbo].[{sanitizedTableName}]
                WHERE workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = CTE.NewOrderNr
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN CTE ON s.[NAME] = CTE.[NAME] 
            and 
            (
                (s.settingtype = 'SettingGroup' AND CTE.[VALUE] = s.[VALUE])
                OR (s.settingtype <> 'SettingGroup' AND CTE.[NAME] = s.[NAME])
            )
            and s.workercode='ALL'";

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error renumbering order numbers in {sanitizedTableName}: {ex.Message}", ex);
        }
    }

    private async Task<bool> FixGroupSettingsOrdernumbers(ProcessType processType, string tableName, string groupSettingName, string groupType)
    {
        ArgumentNullException.ThrowIfNull(processType.ProcessTypeName);
        string sanitizedTableName = ProcessType.SanitizeName(tableName);
        var groupSettingNamePrefix = groupSettingName + ".";    
        using var connection = connectionProvider.GetConnection(processType.ClientName);

        var groupSettingCountSql = $@"SELECT COUNT(*) from [dbo].[{sanitizedTableName}] where [NAME]=@groupSettingName or [NAME] like @groupSettingNamePrefix and workercode='ALL'";
        var groupSettingCount = await connection.ExecuteScalarAsync<int>(groupSettingCountSql, new { groupSettingName, groupSettingNamePrefix  = groupSettingNamePrefix + "%" });
        var groupSettingsMinOrderNrSql = $@"SELECT MIN([ORDERNR]) from [dbo].[{sanitizedTableName}] where [NAME]=@groupSettingName or [NAME] like @groupSettingNamePrefix and workercode='ALL'";
        var minOrderNr = await connection.ExecuteScalarAsync<int>(groupSettingsMinOrderNrSql, new { groupSettingName, groupSettingNamePrefix = groupSettingNamePrefix+"%" });
        var moveOthersDownSql = $@"UPDATE [dbo].[{sanitizedTableName}] SET [ORDERNR] = [ORDERNR] + @groupSettingCount WHERE [NAME]<> @groupSettingName AND [NAME] not like @groupSettingNamePrefix AND [ORDERNR] <= @minOrderNr+@groupSettingCount and workercode='ALL'";
        await connection.ExecuteAsync(moveOthersDownSql, new { groupSettingCount, minOrderNr, groupSettingName, groupSettingNamePrefix = groupSettingNamePrefix + "%" });
        
        // get groupsettings with ordernr ordered by ordernr 
        string groupSettingsSql;
        string updateGroupSettingOrderNrSql;
        string updateGroupSettingMembersOrderNrSql;
        if (groupType == GroupTypeColumn)
        {
            groupSettingsSql = $@"SELECT [VALUE] from [dbo].[{sanitizedTableName}] where [NAME]=@groupSettingName and [SETTINGTYPE]='SettingGroup' and workercode='ALL' order by [ORDERNR]";
            updateGroupSettingOrderNrSql = $@"UPDATE [dbo].[{sanitizedTableName}] SET [ORDERNR] = @currentOrderNr WHERE [NAME]=@name and [VALUE]=@value and [SETTINGTYPE]='SettingGroup' and workercode='ALL'";
            updateGroupSettingMembersOrderNrSql = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR] asc,[NAME] asc) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @namePrefix and workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @currentOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                (s.settingtype = 'SettingGroup' AND RowsToUpdate.[VALUE] = s.[VALUE])
                OR (s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME])
            )
            and s.workercode='ALL'";
        }
         else
        {
            groupSettingsSql = $@"SELECT [NAME] from [dbo].[{sanitizedTableName}] where [NAME] like @groupSettingName+'.%' and [SETTINGTYPE]='SettingGroup' and workercode='ALL' order by [ORDERNR]";
            updateGroupSettingOrderNrSql = $@"UPDATE [dbo].[{sanitizedTableName}] SET [ORDERNR] = @currentOrderNr WHERE [NAME]=@value and [SETTINGTYPE]='SettingGroup' and workercode='ALL'";
            updateGroupSettingMembersOrderNrSql = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR] asc,[NAME] asc) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @value+'%' and workercode='ALL' and [settingtype]<>'SettingGroup'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @currentOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME]
            )
            and s.workercode='ALL'";
        }
        
        var groupSettings = await connection.QueryAsync<string>(groupSettingsSql, new { groupSettingName });
        var currentOrderNr = minOrderNr;
        
        
        foreach (var groupSetting in groupSettings)
        {
            await connection.ExecuteAsync(updateGroupSettingOrderNrSql, new { currentOrderNr, name = groupSettingName, value = groupSetting });
            var updated = await connection.ExecuteAsync(updateGroupSettingMembersOrderNrSql, new { currentOrderNr, namePrefix = groupSettingName+"."+groupSetting + ".%", value=groupSetting });
            currentOrderNr += updated + 1;
        }
        return true;
    }
    
    public async Task<(bool succes, string message)> MoveSettingGroupNodeUp(ProcessType processType, SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(processType.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(node.StoreTable);
        ArgumentNullException.ThrowIfNull(node.FullPath);
        if (! node.ParentIsGroup)
        {
            return (false, "Only groups can be moved.");
        }
        var sanitizedTableName = ProcessType.SanitizeName(node.StoreTable);
        if (sanitizedTableName == ServerSettingsTableName)
        {
            return (false, "Groups in ServerProcessSettings cannot be moved");
        }

        var pathParts = SettingTreeNode.SplitPathToTwo(node.FullPath);

        if (pathParts.prefix == null || pathParts.node == null)
        {
            return (false, "Nodes with less than 2 name parts cannot be moved");
        }

        var groupName = pathParts.prefix;
            
        if (string.IsNullOrEmpty(groupName))
        {
            return (false, "Invalid group name.");
        }

        var nodeName = pathParts.node;
        // Detail style groups are keyed by [NAME], all others by [NAME]+[VALUE].
        var grouptype = SettingTreeNode.IsNameKeyedGroup(node.FullPath) ? GroupTypeGroup : GroupTypeColumn;

        var moved = await FixGroupSettingsOrdernumbers(processType, sanitizedTableName, groupName, grouptype);
        using var connection = connectionProvider.GetConnection(processType.ClientName);

        string sqlGetOrderNr;
        string sqlGetPreviousGroupSql;
        string sqlGetCurrentGroupMembercountSql;
        if (grouptype == "Column")
        {
            sqlGetOrderNr = $@"SELECT [ORDERNR] FROM [dbo].[{sanitizedTableName}] WHERE [NAME]=@groupName and [VALUE]=@nodeName and [SETTINGTYPE]='SettingGroup' and workercode='ALL'";
            sqlGetPreviousGroupSql = $@"SELECT TOP 1 [ORDERNR],[VALUE] FROM [dbo].[{sanitizedTableName}] WHERE [NAME]=@groupName and [SETTINGTYPE]='SettingGroup' and [ORDERNR] < @currentOrderNr  and workercode='ALL' ORDER BY [ORDERNR] DESC";
            sqlGetCurrentGroupMembercountSql = $@"SELECT COUNT(*) FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @groupName+'.'+@nodeName+'.%' and [SETTINGTYPE]<>'SettingGroup' and workercode='ALL'";
        } else {
            sqlGetOrderNr = $@"SELECT [ORDERNR] FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @groupName+'.'+@nodeName and [SETTINGTYPE]='SettingGroup' and workercode='ALL'";
            sqlGetPreviousGroupSql = $@"SELECT TOP 1 [ORDERNR],[VALUE],[NAME] FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @groupName+'.%' and [SETTINGTYPE]='SettingGroup' and [ORDERNR] < @currentOrderNr  and workercode='ALL' ORDER BY [ORDERNR] DESC";
            sqlGetCurrentGroupMembercountSql = $@"SELECT COUNT(*) FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @groupName+'.'+@nodeName+'.%' and [SETTINGTYPE]<>'SettingGroup' and workercode='ALL'";
        }

        var currentOrderNr = await connection.QuerySingleOrDefaultAsync<int?>(sqlGetOrderNr, new { groupName, nodeName });
        var currentGroupMemberCount = await connection.QuerySingleAsync<int>(sqlGetCurrentGroupMembercountSql, new { groupName, nodeName });
        var previousGroup = await connection.QuerySingleOrDefaultAsync<(int? ordernr, string? value, string? name)>(sqlGetPreviousGroupSql, new { groupName, currentOrderNr });

        if (currentOrderNr == null)
        {
            return (false, "Node not found.");
        }
        if (previousGroup.ordernr == null)
        {
            return (false, "Node is already at the top.");
        }

        string sqlMoveGroupUp;
        string sqlMoveGroupMembersUp;
        string sqlMovePrevGroupDown;
        string sqlMovePrevGroupMembersDown;
        if (grouptype == "Column")
        {
            sqlMoveGroupUp = $@"
            UPDATE [dbo].[{sanitizedTableName}] 
            SET [ORDERNR] = @previousOrderNr
            WHERE [NAME] = @groupName AND [VALUE] = @nodeName AND [SETTINGTYPE] = 'SettingGroup' and workercode='ALL'
            ";
            sqlMoveGroupMembersUp = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR]) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @groupPrefix and workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @previousOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                (s.settingtype = 'SettingGroup' AND RowsToUpdate.[VALUE] = s.[VALUE])
                OR (s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME])
            )
            and s.workercode='ALL'
            ";
            sqlMovePrevGroupDown = $@"
            UPDATE [dbo].[{sanitizedTableName}] 
            SET [ORDERNR] = @newOrderNr
            WHERE [NAME] = @groupName AND [VALUE] = @previousGroupValue AND [SETTINGTYPE] = 'SettingGroup' and workercode='ALL'
            ";
            sqlMovePrevGroupMembersDown = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR]) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @prevGroupPrefix and workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @newOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                (s.settingtype = 'SettingGroup' AND RowsToUpdate.[VALUE] = s.[VALUE])
                OR (s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME])
            )
            and s.workercode='ALL'
            ";
        } else
        {
            sqlMoveGroupUp = $@"
            UPDATE [dbo].[{sanitizedTableName}] 
            SET [ORDERNR] = @previousOrderNr
            WHERE [NAME] like @groupName+'.'+@nodeName AND [SETTINGTYPE] = 'SettingGroup' and workercode='ALL'
            ";
            sqlMoveGroupMembersUp = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR]) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @groupPrefix and workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @previousOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME]
            )
            and s.workercode='ALL'
            ";
            sqlMovePrevGroupDown = $@"
            UPDATE [dbo].[{sanitizedTableName}] 
            SET [ORDERNR] = @newOrderNr
            WHERE [NAME] = @previousGroupName AND [SETTINGTYPE] = 'SettingGroup' and workercode='ALL'
            ";
            sqlMovePrevGroupMembersDown = $@"
            WITH RowsToUpdate AS (
                SELECT 
                    [NAME],
                    [VALUE],
		            [settingtype],
                    ROW_NUMBER() OVER (ORDER BY [ORDERNR]) AS RowNum
                FROM [dbo].[{sanitizedTableName}]
                WHERE [NAME] LIKE @previousGroupName+'.%' and [SETTINGTYPE]<>'SettingGroup' and workercode='ALL'
            )
            UPDATE [dbo].[{sanitizedTableName}]
            SET [ORDERNR] = @newOrderNr + RowsToUpdate.RowNum
            FROM [dbo].[{sanitizedTableName}] s
            INNER JOIN RowsToUpdate ON s.[NAME] = RowsToUpdate.[NAME] 
            and 
            (
                s.settingtype <> 'SettingGroup' AND RowsToUpdate.[NAME] = s.[NAME]
            )
            and s.workercode='ALL'
            ";
        }

        var newOrderNr = previousGroup.ordernr + currentGroupMemberCount+1;
        await connection.ExecuteAsync(sqlMoveGroupUp, new { previousOrderNr = previousGroup.ordernr, groupName, nodeName });
        await connection.ExecuteAsync(sqlMoveGroupMembersUp, new { previousGroup, previousOrderNr = previousGroup.ordernr, groupPrefix = groupName +'.'+nodeName+ ".%" });
        await connection.ExecuteAsync(sqlMovePrevGroupDown, new {  newOrderNr = newOrderNr, groupName, previousGroupValue = previousGroup.value, previousGroupName = previousGroup.name });
        await connection.ExecuteAsync(sqlMovePrevGroupMembersDown, new { newOrderNr = newOrderNr, prevGroupPrefix = groupName + "." + previousGroup.value + ".%", previousGroupName = previousGroup.name });

        return (true, "Moved");
    }

    public async Task<(bool success, string message)> DeleteNode(ProcessType forProcess, SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(node.StoreTable);
        return await DeleteGroupInstance(forProcess, node.StoreTable, node.FullPath);
    }

    /// <summary>
    /// Deletes a group instance: every member (named &lt;groupPath&gt;.*) and the SettingGroup row
    /// itself. The group row is identified either by [NAME] alone (Detail style, where the row name
    /// is the whole group path) or by [NAME]+[VALUE] (Column/Field/Command/Report style).
    /// </summary>
    private async Task<(bool success, string message)> DeleteGroupInstance(ProcessType forProcess, string storeTable, string? groupPath)
    {
        var (prefix, value) = SettingTreeNode.SplitPathToTwo(groupPath ?? string.Empty);
        if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(value))
        {
            return (false, "Invalid node full path.");
        }
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var groupName = groupPath!;
        var nameprefix = groupPath + ".%";
        if (sanitizedTableName == ServerSettingsTableName)
        {
            nameprefix = $"{forProcess.Name}.{nameprefix}";
            prefix = $"{forProcess.Name}.{prefix}";
            groupName = $"{forProcess.Name}.{groupName}";
        }
        var groupRowCondition = SettingTreeNode.IsNameKeyedGroup(groupPath)
            ? "[NAME] = @groupName"
            : "[NAME] = @prefix and [VALUE] = @value";
        var sql = $@"DELETE FROM [dbo].[{sanitizedTableName}]
            WHERE workercode='ALL'
            and (
                [NAME] like @nameprefix
                or ([SettingType]='SettingGroup' and {groupRowCondition})
            )";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            nameprefix,
            prefix,
            value,
            groupName
        });

        if (rowsAffected == 0)
        {
            return (false, "Nothing was deleted, node not found.");
        }
        await LogSettingChange(forProcess, sanitizedTableName, groupName, null, "SettingGroup Deleted");
        return (true, $"Deleted {rowsAffected} settings.");
    }

    public async Task<(bool success, string message)> DeleteSubNodes(ProcessType forProcess, SettingTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(node.StoreTable);


        var sanitizedTableName = ProcessType.SanitizeName(node.StoreTable);
        if (sanitizedTableName == "ServerProcessSettings")
        {
            node.Name = $"{forProcess.Name}.{node.Name}";
        }
        var sql = $@"DELETE FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @nameprefix";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            nameprefix = node.FullPath + ".%"
        });



        return (true, "Deleted");
    }

    public async Task<(bool success, string message)> DeleteSetting(ProcessType forProcess, Setting setting)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(setting.StoreTable);
        if (setting.SettingType == FeatureTypes.SettingGroup)
        {
            // A SettingGroup row owns its members; deleting it by [NAME] would orphan them and,
            // for [NAME]+[VALUE] keyed groups, would also wipe every sibling group instance.
            return await DeleteGroupInstance(forProcess, setting.StoreTable, SettingTreeNode.GetGroupPath(setting));
        }
        var sanitizedTableName = ProcessType.SanitizeName(setting.StoreTable);
        if (sanitizedTableName == "ServerProcessSettings")
        {
            setting.Name = $"{forProcess.Name}.{setting.Name}";
        }
        var sql = $@"DELETE FROM [dbo].[{sanitizedTableName}] WHERE [NAME] = @Name and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            setting.Name
        });
        if (rowsAffected > 0)
        {
            await LogSettingChange(forProcess, sanitizedTableName, setting.Name, null, "Setting Deleted");
            return (true, "Setting deleted successfully.");
        }
        else
        {
            return (false, "Setting not found.");
        }
    }

    public async Task<int> GetMaxOrderNr(ProcessType forProcess, string storeTable, string? settingprefix)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        if (settingprefix == null)
        {
            var sqlAll = $@"SELECT coalesce(MAX([ORDERNR]),0) FROM [dbo].[{sanitizedTableName}] and workercode='ALL'";
            var maxOrderNrAll = await connection.QuerySingleAsync<int>(sqlAll);
            return maxOrderNrAll;
        }
        else
        {
            if (sanitizedTableName == ServerSettingsTableName)
            {
                settingprefix = $"{forProcess.Name}.{settingprefix}";
            }
            var prefix = Setting.SanitizeName(settingprefix) + "%";
            var sql = $@"SELECT coalesce(MAX([ORDERNR]),0)   FROM [dbo].[{sanitizedTableName}] where [NAME] like @prefix and workercode='ALL'";
            var maxOrderNr = await connection.QuerySingleAsync<int>(sql, new { prefix });
            return maxOrderNr;
        }

    }

    public async Task<int> GetMinOrderNr(ProcessType forProcess, string storeTable, string? settingprefix)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        if (settingprefix == null)
        {
            var sqlAll = $@"SELECT coalesce(MIN([ORDERNR]),0) FROM [dbo].[{sanitizedTableName}] and workercode='ALL'";
            var minOrderNrAll = await connection.QuerySingleAsync<int>(sqlAll);
            return minOrderNrAll;
        }
        else
        {
            if (sanitizedTableName == ServerSettingsTableName)
            {
                settingprefix = $"{forProcess.Name}.{settingprefix}";
            }
            var prefix = Setting.SanitizeName(settingprefix) + "%";
            var sql = $@"SELECT MIN([ordernr]) FROM [dbo].[{sanitizedTableName}] where [NAME] like @prefix and workercode='ALL'";
            var minOrderNr = await connection.QuerySingleAsync<int>(sql, new { prefix });
            return minOrderNr;
        }

    }

    private async Task<bool> SettingTableExists(ProcessType forProcess, string storeTable)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        // query from INFORMATION_SCHEMA.TABLES to check if table exists

        var sql = $@"select count(*) from INFORMATION_SCHEMA.TABLES where TABLE_NAME=@sanitizedTableName";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedTableName });
        return count > 0;
    }

    private async Task<bool> SettingLogTableExists(ProcessType forProcess)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = SettingsLogTableName;
        // query from INFORMATION_SCHEMA.TABLES to check if table exists

        var sql = $@"select count(*) from INFORMATION_SCHEMA.TABLES where TABLE_NAME=@sanitizedTableName";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedTableName });
        return count > 0;
    }

    private async Task<bool> SettingSessionTableExists(ProcessType forProcess)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = SettingSessionTableName;
        // query from INFORMATION_SCHEMA.TABLES to check if table exists

        var sql = $@"select count(*) from INFORMATION_SCHEMA.TABLES where TABLE_NAME=@sanitizedTableName";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedTableName });
        return count > 0;
    }

    private async Task<bool> SettingExists(ProcessType forProcess, string storeTable, string settingName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var sanitizedSettingName = Setting.SanitizeName(settingName);
        if (sanitizedTableName == ServerSettingsTableName)
        {
            sanitizedSettingName = $"{forProcess.Name}.{sanitizedSettingName}";
        }
        var sql = $@"SELECT COUNT(*) FROM [dbo].[{sanitizedTableName}] WHERE [NAME] = @sanitizedSettingName and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedSettingName });
        return count > 0;
    }

    private async Task<bool> SettingGroupExists(ProcessType forProcess, string storeTable, string settingName, string settingValue)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var sanitizedSettingName = Setting.SanitizeName(settingName);
        if (sanitizedTableName == ServerSettingsTableName)
        {
            sanitizedSettingName = $"{forProcess.Name}.{sanitizedSettingName}";
        }
        var sql = $@"SELECT COUNT(*) FROM [dbo].[{sanitizedTableName}] WHERE [NAME] = @sanitizedSettingName and [VALUE] = @settingValue and [settingtype]='SettingGroup' and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedSettingName, settingValue });
        return count > 0;
    }

    private async Task<bool> ParentSettingExists(ProcessType forProcess, string storeTable, string settingName)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var sanitizedSettingName = Setting.SanitizeName(settingName);
        if (sanitizedTableName == ServerSettingsTableName)
        {
            sanitizedSettingName = $"{forProcess.Name}.{sanitizedSettingName}";
        }
        var sql = $@"SELECT COUNT(*) FROM [dbo].[{sanitizedTableName}] WHERE [NAME] like @sanitizedSettingName and workercode='ALL'";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var count = await connection.QuerySingleAsync<int>(sql, new { sanitizedSettingName = sanitizedSettingName +".%" });
        return count > 0;
    }

    private async Task<bool> CreateSettingsTable(ProcessType forProcess, string storeTable)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);
        var sql = $@"
        CREATE TABLE [dbo].[{sanitizedTableName}](
            [NAME] [nvarchar](200) NOT NULL,
			[VALUE] [nvarchar](400) NOT NULL,
			[settingtype] [nvarchar](20) NOT NULL,
			[LISTVALUE] [nvarchar](1600) NULL,
			[workercode] [nvarchar](100) NOT NULL,
			[ordernr] [int] NOT NULL)
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CreateSettingsLogTable(ProcessType forProcess)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = SettingsLogTableName;
        var sql = $@"
        CREATE TABLE [dbo].[{sanitizedTableName}](
            [TS] datetime2 not null default(getdate()),
            [TABLENAME] [nvarchar](100) NOT NULL,
            [NAME] [nvarchar](200) NOT NULL,
			[VALUE] [nvarchar](400) NULL,
			[settingtype] [nvarchar](20) NOT NULL,
			[LISTVALUE] [nvarchar](1600) NULL,
			[workercode] [nvarchar](100) NOT NULL,
            [session] nvarchar(100) null
			)
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CreateSettingsSessionTable(ProcessType forProcess)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = SettingSessionTableName;
        var sql = $@"
        CREATE TABLE [dbo].[{sanitizedTableName}](
            [TS] datetime2 not null default(getdate()),
            [processname] [nvarchar](100) NOT NULL,
            [workercode] [nvarchar](100) NOT NULL,
            [session] nvarchar(100) not null
			)
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private async Task<bool> LogSettingChange(ProcessType forProcess, string tableName, string settingName, string? settingValue, string settingType = "Setting")
    {
        var result = await EnsureSettingLogTable(forProcess);
        if (!result.success) return false;
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var currentSession = await GetCurrentSettingsSession(forProcess);
        var sql = $@"INSERT INTO [dbo].[{SettingsLogTableName}] ([TABLENAME],[NAME],[VALUE],[SETTINGTYPE],[LISTVALUE],[WORKERCODE],[SESSION]) VALUES (@TableName,@Name,coalesce(@Value,''),@SettingType,null,ORIGINAL_LOGIN(),@Session)";
        try
        {
            await connection.ExecuteAsync(sql, new
            {
                TableName = tableName,
                Name = settingName,
                Value = settingValue,
                SettingType = settingType,
                Session = currentSession
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetCurrentSettingsSession(ProcessType forProcess)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = SettingSessionTableName;
        var sql = $@"SELECT TOP 1 [session] FROM [dbo].[{sanitizedTableName}] WHERE processname=@processname and workercode=ORIGINAL_LOGIN() and TS>GETDATE()-3 order by TS desc";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            var session = await connection.QuerySingleOrDefaultAsync<string>(sql, new { processname = forProcess.Name });
            return session;
        }
        catch // the table may not exist, it will be created when the session is set, so we can just return null here and not log the error
        {
            return null;
        }
    }

    public async Task<(bool success, string message)> SetCurrentSettingsSession(ProcessType forProcess, string session)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        await EnsureSettingLogTable(forProcess);
        var sanitizedTableName = SettingSessionTableName;
        var sql = $@"INSERT INTO [dbo].[{sanitizedTableName}] ([processname],[workercode],[session]) VALUES (@processname,ORIGINAL_LOGIN(),@session)";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(sql, new { processname = forProcess.Name, session });
            return (true, "Session set successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to set session: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> EnsureSettingTable(ProcessType forProcess, string storeTable)
    {
        try
        {
            bool success = true;
            if (!await SettingTableExists(forProcess, storeTable))
            {
                success = await CreateSettingsTable(forProcess, storeTable);
            }
            return (success, "ok");
        }
        catch (Exception ex)
        {
            return (false, $"Error ensuring setting table: {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> EnsureSettingLogTable(ProcessType forProcess)
    {
        try
        {
            bool success = true;
            if (!await SettingLogTableExists(forProcess))
            {
                success = await CreateSettingsLogTable(forProcess);
            }
            if (!await SettingSessionTableExists(forProcess))
            {
                success = success && await CreateSettingsSessionTable(forProcess);
            }
            return (success, "ok");
        }
        catch (Exception ex)
        {
            return (false, $"Error ensuring setting table: {ex.Message}");
        }
    }

    private async Task<bool> ClearSettingsTable(ProcessType forProcess, string storeTable)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        var sanitizedTableName = ProcessType.SanitizeName(storeTable);

        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        if (storeTable == ServerSettingsTableName)
        {

            var sql = $@"DELETE FROM [dbo].[ServerProcessSettings] WHERE [NAME] like @processname+'.%'";
            try
            {
                await connection.ExecuteAsync(sql, new { processname = forProcess.Name });
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            var sql = $@"DELETE FROM [dbo].[{sanitizedTableName}]";
            try
            {
                await connection.ExecuteAsync(sql);
                return true;
            }
            catch
            {
                return false;
            }
        }

    }

    public async Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, TableInfo table, string settingName, string settingValue, string settingType = "Setting")
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(settingName);
        var storeTable = table.TableName;
        ArgumentNullException.ThrowIfNull(storeTable);
        var (tableExists, tableMessage) = await EnsureSettingTable(forProcess, storeTable);
        if (!tableExists)
        {
            return new FeatureAddResult(false, $"Error ensuring settings table: {tableMessage}", ReloadOption.None);
        }
        if (settingType == "Setting")
        {
            if (await SettingExists(forProcess, storeTable, settingName))
            {
                return new FeatureAddResult(false, $"Setting {settingName} already exists in table {storeTable}.", ReloadOption.None);
            }
        }
        if (settingType == "SettingGroup")
        {
            if (await SettingGroupExists(forProcess, storeTable, settingName,settingValue))
            {
                return new FeatureAddResult(false, $"Setting {settingName} already exists in table {storeTable}.", ReloadOption.None);
            }
        }
        var settingNameParts = settingName.Split('.');
        int insertPosition = 0;
        var parentSettingName = String.Empty;
        for (int i = settingNameParts.Length - 1; i > 0; i--)
        {
            var possibleParentSettingName = string.Join('.', settingNameParts.Take(i));
            var maxOrderNr = await GetMaxOrderNr(forProcess, storeTable, possibleParentSettingName);
            if (maxOrderNr != 0)
            {
                insertPosition = maxOrderNr + 1;
                parentSettingName = possibleParentSettingName;
                break;
            }
        }
        if (insertPosition != 0)
        {
            await UpdateOrderNr(forProcess, storeTable, insertPosition, 1);
        }

        var newSetting = new Setting()
        {
            Name = settingName,
            Value = settingValue,
            SettingType = settingType,
            StoreTable = storeTable,
            OrderNr = insertPosition
        };
        var (insertSuccess, insertMessage) = await InsertSetting(forProcess, newSetting);

        var result = new FeatureAddResult(insertSuccess,
             insertSuccess ? $"New setting applied successfully at position {insertPosition}." : insertMessage,
             insertPosition == 0 ? ReloadOption.ReloadAllSettings : ReloadOption.ReloadFromParent);

        if (insertSuccess && !string.IsNullOrEmpty(parentSettingName))
        {
            result.ParentSettingName = parentSettingName;
        }

        return result;
    }

    public async Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, SettingTreeNode parentSettingNode, NewChildArgs args)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(parentSettingNode);
        ArgumentNullException.ThrowIfNull(args);
        var storeTable = parentSettingNode.StoreTable;
        var settingName = args.SettingName;
        ArgumentNullException.ThrowIfNull(storeTable);
        ArgumentNullException.ThrowIfNull(parentSettingNode.FullPath);

        ArgumentNullException.ThrowIfNull(parentSettingNode.Setting);

        var (tableExists, tableMessage) = await EnsureSettingTable(forProcess, parentSettingNode.StoreTable);
        if (!tableExists)
        {
            return new FeatureAddResult(false, $"Error ensuring settings table: {tableMessage}", ReloadOption.None);
        }
        var parentPath = SettingTreeNode.GetPathInfo(parentSettingNode.Setting).ParentPath;
        var newSettingName = $"{parentPath}.{settingName}";

        if (await SettingExists(forProcess, storeTable, newSettingName))
        {
            return new FeatureAddResult(false, $"Setting {newSettingName} already exists under {parentSettingNode.Name}.", ReloadOption.None);
        }
        var nr = await GetMinOrderNr(forProcess, storeTable, parentSettingNode.FullPath);

        // calculate the new setting full path by splitting parentSettingNode.FullPath and join with . except last


        var amount = 1;
        await UpdateOrderNr(forProcess, storeTable, nr + 1, amount); // make space for 
        int orderNrOffset = nr + 1;
        var newSetting = new Setting()
        {
            Name = newSettingName,
            Value = args.SettingValue,
            SettingType = "Setting",
            StoreTable = storeTable,
            OrderNr = orderNrOffset
        };
        var (insertSuccess, insertMessage) = await InsertSetting(forProcess, newSetting);
        return new FeatureAddResult(true, $"New setting applied successfully {nr}.", ReloadOption.ReloadFromParent);
    }

    public async Task<FeatureAddResult> AddNewSetting(ProcessType forProcess, SettingTreeNode parentSettingNode, NewSettingArgs args)
    {
        ArgumentNullException.ThrowIfNull(forProcess.ProcessTypeName);
        ArgumentNullException.ThrowIfNull(parentSettingNode);
        ArgumentNullException.ThrowIfNull(args);
        var defaultStoreTable = parentSettingNode.StoreTable;
        var featureName = args.FeatureName;
        var newSettingName = args.NewSettingName;
        bool lookupParent = true;
        ArgumentNullException.ThrowIfNull(defaultStoreTable);
        ArgumentNullException.ThrowIfNull(featureName);

        ArgumentNullException.ThrowIfNull(parentSettingNode.FullPath);

        var feature = await GetSettingFeature(forProcess, parentSettingNode, featureName);

        var (tableExists, tableMessage) = await EnsureSettingTable(forProcess, parentSettingNode.StoreTable);
        if (!tableExists)
        {
            return new FeatureAddResult(false, $"Error ensuring settings table: {tableMessage}", ReloadOption.None);
        }

        var featureValues = await GetSettingFeatureValues(forProcess, feature.FeatureName);
        int? ordernr= null;
        
        switch (feature.FeatureType)
        {
            case FeatureTypes.SettingGroup:
                ArgumentNullException.ThrowIfNull(newSettingName);
                var storeTable = GetTableNameForSettingGroup(parentSettingNode, feature, forProcess);
                var groupType = SettingTreeNode.IsNameKeyedGroup(feature.ParentSettingName) ? GroupTypeGroup : GroupTypeColumn;
                var fixsuccess = await FixGroupSettingsOrdernumbers(forProcess, storeTable, feature.ParentSettingName, groupType);
                int moveFrom = args.position switch
                {
                    PositionOption.First => await GetMinOrderNr(forProcess, storeTable, parentSettingNode.FullPath),
                    PositionOption.Last => (await GetMaxOrderNr(forProcess, storeTable, $"{parentSettingNode.FullPath}.")) + 1,
                    PositionOption.After => await GetMaxOrderNr(forProcess, storeTable, $"{parentSettingNode.FullPath}.{args.AfterSettingName}.") + 1,
                    _ => throw new ArgumentOutOfRangeException($"Unknown position option: {args.position}")
                };



                var amount = featureValues.Count() + 1;
                await UpdateOrderNr(forProcess, storeTable, moveFrom, amount);
                var orderNrOffset = moveFrom;
                
                ordernr = orderNrOffset;
                lookupParent = false;

                //var newSettingFirst = new Setting()
                //{
                //    Name = parentSettingNode.FullPath,
                //    Value = newSettingName,
                //    SettingType = FeatureTypes.SettingGroup,
                //    StoreTable = storeTable,
                //    OrderNr = orderNrOffset
                //};
                //var (insertSuccessFirst, insertMessageFirst) = await InsertSetting(forProcess, newSettingFirst);
                
                break;
            case FeatureTypes.SettingReplace:
            case FeatureTypes.SettingUpdate:
                break;
            default:
                return new FeatureAddResult(false, $"Feature type '{feature.FeatureType}' not supported for adding new setting.", ReloadOption.None);
        }


        foreach (var featureValue in featureValues.Where(fv => fv.SettingValueType == SettingValueType.Remove))
        {
            var (success, message) = await RemoveSettingFeatureValue(forProcess, parentSettingNode, feature, featureValue);
        }
        foreach (var featureValue in featureValues.Where(fv => fv.SettingValueType != SettingValueType.Remove))
        {
            var (success, message) = await AddSettingFeatureValue(forProcess, parentSettingNode, newSettingName, feature, featureValue, ordernr is null ? null : ++ordernr, lookupParent);
        }
        return new FeatureAddResult(true, $"New setting applied successfully.", ReloadOption.ReloadSetting);

        string GetTableNameForSettingGroup(SettingTreeNode parentSettingNode, SettingFeature feature, ProcessType forProcess)
        {       
            return feature.TableName switch
            {
                ServerSettingsTableName => ServerSettingsTableName,
                not null and not "" => $"{forProcess.Name}{feature.TableName}",
                _ => parentSettingNode.StoreTable
            };
        }
        
    }

    private async Task<(bool success,string message)> AddSettingFeatureValue(ProcessType forProcess, SettingTreeNode parentSettingNode, string? newSettingName, SettingFeature feature, SettingFeatureValue featureValue, int? ordernr, bool lookupParent)
    {
        var updatableParentName = featureValue.ParentSettingName.Replace("{{parentname}}", parentSettingNode.Name);
        updatableParentName = updatableParentName.Replace("{{settingname}}", newSettingName);
        var storeTable = featureValue.StoreTable switch
        {
            ServerSettingsTableName => ServerSettingsTableName,
            not null and not "" => $"{forProcess.Name}{featureValue.StoreTable}",
            //_ => parentSettingNode.StoreTable
            _ => $"{forProcess.Name}{feature.TableName}"
        };


        if (!String.IsNullOrEmpty(featureValue.ParentSettingName) && lookupParent)
        {
            bool parentExists = await ParentSettingExists(forProcess, storeTable, updatableParentName);
            if (!parentExists)
                return (true, "Skipped, parent does not exist");
        }

        //var updatableSettingName = $"{updatableParentName}.{featureValue.SettingName}";

        var updatableSettingName = featureValue.ParentSettingName switch
        {
            "" or null => featureValue.SettingName,
            _ => $"{featureValue.ParentSettingName}.{featureValue.SettingName}"
        };

        updatableSettingName = updatableSettingName.Replace("{{parentname}}", parentSettingNode.Name);
        updatableSettingName = updatableSettingName.Replace("{{parentpath}}", parentSettingNode.FullPath);
        updatableSettingName = !String.IsNullOrEmpty(newSettingName) ?
            updatableSettingName.Replace("{{settingname}}", newSettingName) :
            updatableSettingName.Replace("{{settingname}}", parentSettingNode.Name);

        var settingValue = featureValue.SettingValue;
        if (settingValue != null)
        {
            settingValue = settingValue.Replace("{{settingname}}", newSettingName ?? "");
        }

        bool settingExists;
        if (feature.FeatureType == FeatureTypes.SettingGroup && featureValue.Order == 1)
        {
            settingExists = await SettingGroupExists(forProcess, storeTable, updatableSettingName, settingValue ?? string.Empty);
        }
        else
        {
            settingExists = await SettingExists(forProcess, storeTable, updatableSettingName);
        }

        Console.WriteLine($"{updatableSettingName} => {storeTable}");

        if (settingExists)
        {
            return await SaveSettingValue(forProcess, storeTable, updatableSettingName, featureValue.SettingValue ?? string.Empty);
        }
        else
        {
            int orderNrOffset = ordernr ?? await GetMaxOrderNr(forProcess, storeTable, parentSettingNode.FullPath) + 1;
            if (ordernr == null)
                await UpdateOrderNr(forProcess, storeTable, orderNrOffset, 1);

            
            var settingType = featureValue.Order == 1 && feature.FeatureType == FeatureTypes.SettingGroup ? FeatureTypes.SettingGroup : "Setting";
            var newSetting = new Setting()
            {
                Name = updatableSettingName,
                Value = settingValue ?? string.Empty,
                SettingType = settingType,
                StoreTable = storeTable,
                OrderNr = orderNrOffset
            };
            return await InsertSetting(forProcess, newSetting);
        }
    }

    private async Task<(bool success, string message)> RemoveSettingFeatureValue(ProcessType forProcess, SettingTreeNode parentSettingNode, SettingFeature feature, SettingFeatureValue featureValue)
    {
        var updatableParentName = featureValue.ParentSettingName.Replace("{{parentname}}", parentSettingNode.Name);
        var storeTable = featureValue.StoreTable switch
        {
            ServerSettingsTableName => ServerSettingsTableName,
            not null and not "" => $"{forProcess.Name}{featureValue.StoreTable}",
            //_ => parentSettingNode.StoreTable
            _ => $"{forProcess.Name}{feature.TableName}"
        };
        if (!String.IsNullOrEmpty(featureValue.ParentSettingName))
        {
            bool parentExists = await ParentSettingExists(forProcess, storeTable, updatableParentName);
            if (!parentExists)
                return (true, "Skipped, parent does not exist");
        }
        var updatableSettingName = featureValue.ParentSettingName switch
        {
            "" or null => featureValue.SettingName,
            _ => $"{featureValue.ParentSettingName}"
        };

        updatableSettingName = featureValue.SettingName switch
        {
            "" or null => updatableSettingName,
            _ => $"{updatableSettingName}.{featureValue.SettingName}"
        }; 

        updatableSettingName = updatableSettingName.Replace("{{parentname}}", parentSettingNode.Name);
        updatableSettingName = updatableSettingName.Replace("{{parentpath}}", parentSettingNode.FullPath);
        bool settingExists = await SettingExists(forProcess, storeTable, updatableSettingName);
        if (settingExists)
        {
            return await DeleteSetting(forProcess, new Setting()
            {
                Name = updatableSettingName,
                StoreTable = storeTable
            });
        }
        return (true, "Setting does not exist, nothing to remove.");
    }

    public async Task<(bool success, string message)> CopySettings(ProcessType forProcess, ClientInfo sourceClient, string tableName, IProgress<int>? progress = null)
    {

        if (sourceClient.ClientName == forProcess.ClientName)
        {
            return (false, "Source and target clients cannot be the same.");
        }

        var sanitizedTableName = ProcessType.SanitizeName(tableName);
        var targetTableName = forProcess.Name+ Setting.GetTableWithoutPrefix(sanitizedTableName);

        var ensureResult = await EnsureSettingTable(forProcess, sanitizedTableName);
        if (!ensureResult.success)
        {
            return (false, $"Error ensuring settings table: {ensureResult.message}");
        }
        var clearTableResult = await ClearSettingsTable(forProcess, sanitizedTableName);
        if (!clearTableResult)
        {
            return (false, "Error clearing target settings table.");
        }
        var settingsSql = $"""
            SELECT s.[NAME],s.[VALUE],s.[SETTINGTYPE]
                ,s.[LISTVALUE],s.[ORDERNR],null as FEATURENAME
                ,null as [VALUETYPE],'{targetTableName}' as STORETABLE 
                FROM [dbo].[{sanitizedTableName}] s where s.workercode='ALL' order by s.[ORDERNR] asc
            """;
        var serverSettingsSql = $"""
            SELECT 
                s.[NAME],
                s.[VALUE],
                s.[SETTINGTYPE],
                s.[LISTVALUE],
                s.[ORDERNR],
                null AS FEATURENAME,
                null AS [VALUETYPE],
                'ServerProcessSettings' AS STORETABLE
            FROM [dbo].[ServerProcessSettings] s
            WHERE s.[NAME] LIKE @processname + '.%'
            ORDER BY s.[ORDERNR] ASC
            """;
        using var sourceConnection = connectionProvider.GetConnection(sourceClient.ClientName);
        using var targetConnection = connectionProvider.GetConnection(forProcess.ClientName);

        var settings = sanitizedTableName == ServerSettingsTableName ?
            await sourceConnection.QueryAsync<Setting>(serverSettingsSql, new { processname = forProcess.Name }) :
            await sourceConnection.QueryAsync<Setting>(settingsSql);

        var settingsList = settings.ToList();
        var total = settingsList.Count;
        var count = 0;
        var lastReportedProgress = 0;
        foreach (var setting in settingsList)
        {
            
            var insertResult = await InsertSetting(forProcess, setting);
            ++count;
            var currentProgress = total > 0 ? (int)((count / (double)total) * 100) : 0;
            if (currentProgress >= lastReportedProgress + 5 || currentProgress == 100)
            {
                progress?.Report(currentProgress);
                lastReportedProgress = currentProgress;
            }

            if (!insertResult.success)
            {
                return (false, $"Error inserting setting {setting.Name}: {insertResult.message}");
            }
        }

        return (true, $"{count}/{total} settings copied successfully from {sourceClient.ClientName} to {forProcess.ClientName}.");
    }

    public async Task<(bool success, string message)> BackupSettings(ProcessType forProcess, string tableName, string description = "")
    {
        var sanitizedTableName = ProcessType.SanitizeName(tableName);
        var backupTableName = $"bck_{sanitizedTableName}_{DateTime.UtcNow:yyyyMMdd_HHmm}";
        var ensureResult = await EnsureSettingTable(forProcess, sanitizedTableName);
        if (!ensureResult.success)
        {
            return (false, $"Error ensuring settings table: {ensureResult.message}");
        }

        var createBackupSql = $@"
            SELECT *
            INTO [dbo].[{backupTableName}]
            FROM [dbo].[{sanitizedTableName}];
        ";
        var insertDescriptionSql = $@"
            INSERT INTO [dbo].[{backupTableName}] ([NAME], [VALUE], [SETTINGTYPE], [LISTVALUE], [WORKERCODE], [ORDERNR])
            VALUES ('Backup.Description', @description, 'BackupDescription', null, 'SYSTEM', 100000);
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(createBackupSql);
            await connection.ExecuteAsync(insertDescriptionSql, new { description });
            return (true, $"Backup of table {sanitizedTableName} created successfully as {backupTableName}.");
        }
        catch (Exception ex)
        {
            return (false, $"Error creating backup table: {ex.Message}");
        }
    }

    public async Task<IEnumerable<string>> GetBackupTables(ProcessType forProcess)
    {
        var sanitizedTableName = ProcessType.SanitizeName(forProcess.Name);
        var sql = $@"
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME LIKE 'bck_{sanitizedTableName}_%'
            ORDER BY TABLE_NAME DESC;
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var tables = await connection.QueryAsync<string>(sql);
        return tables;
    }

    public async Task<(bool success, string message)> RestoreBackup(ProcessType forProcess, string tableName, string backupTableName)
    {
        var sanitizedTableName = ProcessType.SanitizeName(tableName);
        var ensureResult = await EnsureSettingTable(forProcess, sanitizedTableName);
        if (!ensureResult.success)
        {
            return (false, $"Error ensuring settings table: {ensureResult.message}");
        }
        var clearTableResult = await ClearSettingsTable(forProcess, sanitizedTableName);
        if (!clearTableResult)
        {
            return (false, "Error clearing target settings table.");
        }
        var restoreSql = $@"
            INSERT INTO [dbo].[{sanitizedTableName}] ([NAME], [VALUE], [SETTINGTYPE], [LISTVALUE], [WORKERCODE], [ORDERNR])
            SELECT [NAME], [VALUE], [SETTINGTYPE], [LISTVALUE], [WORKERCODE], [ORDERNR]
            FROM [dbo].[{backupTableName}]
            WHERE [SETTINGTYPE] <> 'BackupDescription';
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        try
        {
            await connection.ExecuteAsync(restoreSql);
            return (true, $"Backup {backupTableName} restored successfully to table {sanitizedTableName}.");
        }
        catch (Exception ex)
        {
            return (false, $"Error restoring backup table: {ex.Message}");
        }
    }

    public async Task<string?> GetBackupInfo(ProcessType forProcess, string backupTableName)
    {
        var sanitizedTableName = ProcessType.SanitizeName(backupTableName).Replace("bck_", "");
        var sql = $@"
            SELECT 
                top 1 
                [VALUE] AS Description
            FROM [dbo].[bck_{sanitizedTableName}]
            WHERE [NAME] = 'Backup.Description'
            ORDER BY BackupTimestamp DESC;
        ";
        using var connection = connectionProvider.GetConnection(forProcess.ClientName);
        var backupdescription = await connection.QuerySingleOrDefaultAsync<string>(sql);
        return backupdescription;
    }
}