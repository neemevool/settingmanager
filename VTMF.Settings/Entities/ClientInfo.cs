namespace VTMF.Settings.Entities;

public struct ClientInfo(string ClientName)
{
    public string ClientName { get; } = ClientName;
}

