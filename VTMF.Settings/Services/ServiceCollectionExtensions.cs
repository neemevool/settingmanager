namespace VTMF.Settings.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        // add SqlConnectionParameters as a singleton option, read from configuration
        //services.AddSingleton<SqlConnectionParameters>(sp =>
        //{
        //    var configuration = sp.GetRequiredService<IConfiguration>();
        //    var sqlParams = new SqlConnectionParameters();
        //    configuration.GetSection("SqlConnectionParameters").Bind(sqlParams);
        //    return sqlParams;
        //});

        services.AddSingleton<Dictionary<string, SqlConnectionParameters>>(sp =>
        {
            Console.WriteLine("Binding SqlConnectionParameters from configuration...");
            var configuration = sp.GetRequiredService<IConfiguration>();
            var sqlParamsList = new List<SqlConnectionParameters>();
            configuration.GetSection("SqlConnectionParameters").Bind(sqlParamsList);
            // enumerate all settings under SqlConnectionParameters for debugging
            
            return sqlParamsList.ToDictionary(p => p.ClientName ?? throw new InvalidOperationException("ClientName is required for SqlConnectionParameters"));
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISettingsMetadataService, SettingMetaService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<IWorkerService, WorkerService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IWikiImageService, WikiImageService>();
        services.AddSingleton<IFeatureManagementService, FeatureManagementService>();
        services.AddSingleton<SqlConnectionProvider>();

        //services.AddScoped<ISettingsService, SettingsService>();
        return services;
    }
}
