using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VTMF.Settings.Services;

namespace VTMF.Settings.Tests.Core;

public sealed class SqlConnectionParametersTests
{
    [Fact]
    public void ConnectionString_WithCredentials_UsesSqlLogin()
    {
        var parameters = new SqlConnectionParameters { Server = "Server=tcp:db,1433;Database=x", UserId = "u", Password = "p" };

        Assert.Equal("Server=tcp:db,1433;Database=x;User Id=u;Password=p;", parameters.ConnectionString);
    }

    [Fact]
    public void ConnectionString_WithoutCredentials_UsesIntegratedSecurity()
    {
        var parameters = new SqlConnectionParameters { Server = "Server=db" };

        Assert.Equal("Server=db;Integrated Security=True;", parameters.ConnectionString);
    }

    [Fact]
    public void AddSqlConnectionParameters_BindsClientsAndHidesMetabase()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlConnectionParameters:0:ClientName"] = "alpha",
                ["SqlConnectionParameters:0:Server"] = "Server=a",
                ["SqlConnectionParameters:1:ClientName"] = SqlConnectionParameters.DefaultMetaBaseClientName,
                ["SqlConnectionParameters:1:Server"] = "Server=m",
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);

        services.AddSqlConnectionParameters();
        using var provider = services.BuildServiceProvider();

        var connections = provider.GetRequiredService<SqlConnectionProvider>();
        Assert.Equal(["alpha"], connections.GetAvailableClients());
        Assert.Equal("Server=m;Integrated Security=True;", connections.GetConnectionString(SqlConnectionParameters.DefaultMetaBaseClientName));
    }
}
