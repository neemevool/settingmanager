namespace VTMF.Settings.Services;

public class SqlConnectionParameters
{
    public const string DefaultMetaBaseClientName = "DefaultMetaBase";
    private string? connectionString;
    public string? Server { get; set; } = "Server=localhost;";
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public string? ClientName { get; set; }
    public string MetaBaseName { get; set; } = DefaultMetaBaseClientName;
    public string? InstallXmlPath { get; set; } = "install.xml";

    // Installer-related properties
    public string? SqlScriptsDir { get; set; }
    public string? InstallMode { get; set; }
    public string? SqlScriptsPrefix { get; set; }
    public string? ConfigDir { get; set; }
    public string? KeyReceiverUrl { get; set; }
    public string? KeyReceiverToken { get; set; }
    public string? Admins { get; set; }
    public string? TfsToken { get; set; }

    public string ConnectionString
    {
        get {
            EnsureConnectionString();
            return connectionString!;
        }
    }

    public string? DatabaseName
    {
        get
        {
            if (string.IsNullOrEmpty(Server))
                return null;

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? null : builder.InitialCatalog;
        }
    }

    private void EnsureConnectionString()
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            if (!string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(Password))
            {
                connectionString = $"{Server};User Id={UserId};Password={Password};";
            }
            else
            {
                connectionString = $"{Server};Integrated Security=True;";
            }
        }
    }
}
