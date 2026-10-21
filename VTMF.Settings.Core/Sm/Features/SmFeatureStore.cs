using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using VTMF.Settings.Services;

namespace VTMF.Settings.Core.Sm.Features;

/// <summary>Typed access to the feature tables and procedures of the settingsmetabase module.</summary>
public sealed class SmFeatureStore(
    SqlConnectionProvider connections,
    SmMetabaseScripts scripts,
    string connectionName = SqlConnectionParameters.DefaultMetaBaseClientName)
{
    public async Task<SmModuleStatus> GetModuleStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var version = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            IF OBJECT_ID(N'sm.moduleinfo', N'U') IS NULL SELECT CAST(NULL AS int);
            ELSE SELECT version FROM sm.moduleinfo WHERE module = @module;
            """,
            new { module = scripts.Set.ModuleName }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new SmModuleStatus
        {
            Installed = version is not null,
            Version = version,
            AvailableVersion = scripts.Set.Version,
            State = SmModuleStates.From(version, scripts.Set.Version),
        };
    }

    /// <summary>Central version gate for every feature operation except module status.</summary>
    private async Task EnsureUpToDateAsync(CancellationToken cancellationToken)
    {
        var status = await GetModuleStatusAsync(cancellationToken).ConfigureAwait(false);
        switch (status.State)
        {
            case SmModuleStates.UpToDate:
                return;
            case SmModuleStates.NewerThanTool:
                throw new SmException(SmErrorCodes.FeatureModuleNewerThanTool,
                    SmMessages.NewerThanTool(connectionName, status.Version, status.AvailableVersion));
            case SmModuleStates.Outdated:
                throw new SmException(SmErrorCodes.FeatureModuleOutdated,
                    $"The settingsmetabase module is outdated (installed version {status.Version}, available {status.AvailableVersion}); call install_feature_module.");
            default:
                throw new SmException(SmErrorCodes.FeatureModuleNotInstalled,
                    $"The settingsmetabase module is not installed (available version {status.AvailableVersion}); call install_feature_module.");
        }
    }

    public async Task<IReadOnlyList<SmFeatureSummary>> FindFeaturesAsync(
        string? text, string? status = "Finished", CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await connection.QueryAsync<SmFeatureSummary>(new CommandDefinition(
                "sm.FindFeatures", new { text, status }, commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows.AsList();
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }

    public async Task<SmFeature> GetFeatureAsync(int? id, string? name, CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                "sm.GetFeature", new { id, name }, commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var header = await grid.ReadSingleAsync<SmFeature>().ConfigureAwait(false);
            var rows = (await grid.ReadAsync<SmFeatureRow>().ConfigureAwait(false)).AsList();
            var parameters = (await grid.ReadAsync<SmFeatureParam>().ConfigureAwait(false)).AsList();
            var values = (await grid.ReadAsync<ParamValueRow>().ConfigureAwait(false)).AsList();
            var steps = (await grid.ReadAsync<SmFeatureStep>().ConfigureAwait(false)).AsList();

            return new SmFeature
            {
                Id = header.Id,
                Name = header.Name,
                Description = header.Description,
                Status = header.Status,
                BuiltOnProcess = header.BuiltOnProcess,
                Revision = header.Revision,
                CreatedBy = header.CreatedBy,
                Created = header.Created,
                ModifiedBy = header.ModifiedBy,
                Modified = header.Modified,
                Rows = rows,
                Params = parameters.Select(p => new SmFeatureParam
                {
                    Name = p.Name,
                    Description = p.Description,
                    Type = p.Type,
                    Default = p.Default,
                    Values = values.Where(v => string.Equals(v.ParamName, p.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(v => new SmFeatureParamValue { Value = v.Value, Description = v.Description }).ToList(),
                }).ToList(),
                Steps = steps,
            };
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }

    public async Task<SmFeatureSaveResult> SaveFeatureAsync(
        SmFeature feature, long? revision, CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
        feature = WithoutNullCollections(feature);
        var finished = false;
        if (feature.Id is { } existingId)
        {
            var current = await GetFeatureAsync(existingId, null, cancellationToken).ConfigureAwait(false);
            finished = current.Status == "Finished";
        }

        var validation = SmFeatureValidator.Validate(feature, finish: finished);
        ThrowIfInvalid(validation);

        var json = JsonSerializer.Serialize(feature);
        var result = await ExecuteIdRevisionAsync("sm.SaveFeature", new { json, revision }, cancellationToken).ConfigureAwait(false);
        return new SmFeatureSaveResult { Id = result.Id, Revision = result.Revision, Warnings = validation.Warnings };
    }

    /// <summary>Explicit JSON nulls leave collections null; treat them as empty.</summary>
    private static SmFeature WithoutNullCollections(SmFeature feature) => new()
    {
        Id = feature.Id,
        Name = feature.Name,
        Description = feature.Description,
        Status = feature.Status,
        BuiltOnProcess = feature.BuiltOnProcess,
        Revision = feature.Revision,
        CreatedBy = feature.CreatedBy,
        Created = feature.Created,
        ModifiedBy = feature.ModifiedBy,
        Modified = feature.Modified,
        Rows = feature.Rows ?? [],
        Params = (feature.Params ?? []).Select(p => new SmFeatureParam
        {
            Name = p.Name,
            Description = p.Description,
            Type = p.Type,
            Default = p.Default,
            Values = p.Values ?? [],
        }).ToList(),
        Steps = feature.Steps ?? [],
    };

    private static void ThrowIfInvalid(SmFeatureValidation validation)
    {
        if (validation.Errors.Count > 0)
        {
            throw new SmException(SmErrorCodes.FeatureInvalid, string.Join("; ", validation.Errors));
        }
    }

    public async Task<SmFeatureSaveResult> SetFeatureStatusAsync(
        int id, string status, long revision, CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SmPlanWarning> warnings = [];
        if (status == "Finished")
        {
            var feature = await GetFeatureAsync(id, null, cancellationToken).ConfigureAwait(false);
            var validation = SmFeatureValidator.Validate(feature, finish: true);
            ThrowIfInvalid(validation);
            warnings = validation.Warnings;
        }

        var result = await ExecuteIdRevisionAsync("sm.SetFeatureStatus", new { id, status, revision }, cancellationToken).ConfigureAwait(false);
        return new SmFeatureSaveResult { Id = result.Id, Revision = result.Revision, Warnings = warnings };
    }

    public async Task DeleteFeatureAsync(int id, long revision, CancellationToken cancellationToken = default)
    {
        await EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "sm.DeleteFeature", new { id, revision }, commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }

    private async Task<IdRevision> ExecuteIdRevisionAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await connection.QuerySingleAsync<IdRevision>(new CommandDefinition(
                procedure, parameters, commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqlException ex) when (SmException.TryTranslate(ex, out var translated))
        {
            throw translated;
        }
    }

    private sealed class IdRevision
    {
        public int Id { get; set; }
        public long Revision { get; set; }
    }

    private sealed class ParamValueRow
    {
        public string ParamName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection;
        try
        {
            connection = connections.GetConnection(connectionName);
        }
        catch (KeyNotFoundException ex)
        {
            throw new SmException(SmErrorCodes.MetabaseNotConfigured,
                "No DefaultMetaBase connection is configured; features need the shared metadata database.", ex);
        }

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}
