using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
