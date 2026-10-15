using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Headers;
using VTMF.SQLInstaller.Entities;
using System.Collections;
using System.Net.Http.Json;

namespace VTMF.SQLInstaller.Services;

public class KeyReceiverService : IKeyReceiverService
{
    private readonly ILogger<KeyReceiverService> _logger;
    private readonly string _keyReceiverUrl;
    private readonly string _keyReceiverToken;

    private readonly HttpClient _httpClient;
    private readonly SerializationContext _serializationContext;

    public KeyReceiverService(ILogger<KeyReceiverService> logger, string keyReceiverUrl, string keyReceiverToken)
    {
        _logger = logger;
        _keyReceiverUrl = keyReceiverUrl;
        _keyReceiverToken = keyReceiverToken;
        _serializationContext = new SerializationContext();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_keyReceiverUrl.EndsWith("/") ? _keyReceiverUrl : _keyReceiverUrl + "/")
        };
        _httpClient.DefaultRequestHeaders.Add("X-Apikey", _keyReceiverToken);
        _logger.LogInformation("Key Receiver service initialized with URL: {KeyReceiverUrl}", _keyReceiverUrl);
        _logger.LogDebug("Key Receiver service initialized with Token: {KeyReceiverToken}", _keyReceiverToken);
    }

    /// <summary>
    /// Logs an InstallLogs record to the Key Receiver service in the background without blocking or throwing errors.
    /// </summary>
    public void LogToKeyReceiver(InstallLogs log)
    {
        Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Sending log to Key Receiver service: {LogMessage}", log.LogMessage);

                var json = JsonSerializer.Serialize(log, typeof(InstallLogs), _serializationContext);

                var content = new StringContent(json, Encoding.UTF8);

                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var response = await _httpClient.PostAsync("installlogs", content);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to send log. Status Code: {StatusCode}, Response: {Response}",
                        response.StatusCode, await response.Content.ReadAsStringAsync());

                }

            }
            catch (Exception ex)
            {
                // Silently log the error but don't throw - logging failures should not impact the application
                _logger.LogError(ex, "Failed to send log to Key Receiver service");
            }
        });


    }

    /// <summary>
    /// Sends a SQL secret record to the Key Receiver service.
    /// </summary>
    public async Task SendSqlSecret(SqlSecret secret)
    {
        _logger.LogInformation("Sending SQL secret for client: {ClientName}, receiver: {Receiver}, secretname: {SecretName}",
            secret.ClientName, secret.Receiver, secret.SecretName);

        var json = JsonSerializer.Serialize(secret, typeof(SqlSecret), _serializationContext);
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        await _httpClient.PostAsync("sqlsecret", content);
    }

    public async Task<IEnumerable<InstallTask>> GetInstallTasksAsync(string clientName)
    {
        var response = await _httpClient.GetAsync($"installtasks/{clientName}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<List<InstallTask>>(
            SerializationContext.Default.ListInstallTask);

        return Enumerable.Empty<InstallTask>();
    }

    public async Task SetInstallationTaskCompleted(String clientName, Guid taskUuid)
    {
        var response = await _httpClient.PostAsync($"installtasks/{taskUuid}/complete", null);
        response.EnsureSuccessStatusCode();
    }
}

