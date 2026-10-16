namespace VTMF.Settings.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        services.AddSqlConnectionParameters();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISettingsMetadataService, SettingMetaService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<IWorkerService, WorkerService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IWikiImageService, WikiImageService>();
        services.AddSingleton<IFeatureManagementService, FeatureManagementService>();

        //services.AddScoped<ISettingsService, SettingsService>();
        return services;
    }
}
