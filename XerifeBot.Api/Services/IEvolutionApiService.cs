using XerifeBot.Api.Dtos;

namespace XerifeBot.Api.Services;

public interface IEvolutionApiService
{
    Task<bool> SendTextAsync(string instance, string remoteJid, string text);
    Task<bool> SendMediaAsync(string instance, string remoteJid, string mediaUrl, string caption);
    bool IsMessageFromBot(string remoteJid, string? messageId);
    Task<InstanceInfoDto?> GetInstanceInfoAsync(string instanceName);

    // Métodos para gestão dinâmica de instâncias e QR Code (Multi-Tenant)
    Task<bool> CreateInstanceAsync(string instanceName, string? webhookUrl = null);
    Task<WhatsAppConnectResultDto> ConnectInstanceAsync(string instanceName);
    Task<bool> SetWebhookAsync(string instanceName, string webhookUrl);
    Task<bool> LogoutInstanceAsync(string instanceName);
    Task<bool> RestartInstanceAsync(string instanceName);
    Task<bool> DeleteInstanceAsync(string instanceName);
}
