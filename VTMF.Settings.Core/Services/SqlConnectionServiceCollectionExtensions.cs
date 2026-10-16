using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VTMF.Settings.Services;

public static class SqlConnectionServiceCollectionExtensions
{
    public const string ConfigurationSection = "SqlConnectionParameters";

    public static IServiceCollection AddSqlConnectionParameters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var parameters = new List<SqlConnectionParameters>();
            configuration.GetSection(ConfigurationSection).Bind(parameters);
            return parameters.ToDictionary(
                p => p.ClientName ?? throw new InvalidOperationException("ClientName is required for SqlConnectionParameters"),
                StringComparer.Ordinal);
        });
        services.AddSingleton<SqlConnectionProvider>();
        return services;
    }
}
