using Microsoft.Extensions.DependencyInjection;
using VTMF.Settings.Core.Sm.Features;

namespace VTMF.Settings.Core.Sm;

public static class SmServiceCollectionExtensions
{
    public static IServiceCollection AddSmClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SmClient>();
        services.AddSingleton(_ => SmScriptSet.LoadDefault());
        services.AddSingleton<SmInstaller>();
        services.AddSingleton<SmOrchestrator>();
        services.AddSingleton(_ => new SmMetabaseScripts(SmScriptSet.LoadMetabase()));
        services.AddSingleton<SmFeatureInstaller>();
        services.AddSingleton<SmFeatureStore>();
        services.AddSingleton<SmFeaturePlanner>();
        return services;
    }
}
