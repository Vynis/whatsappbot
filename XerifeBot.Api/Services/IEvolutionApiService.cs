using XerifeBot.Api.Dtos;

namespace XerifeBot.Api.Services;

public interface IEvolutionApiService
{
    Task<bool> SendTextAsync(string instance, string remoteJid, string text);
    Task<bool> SendMediaAsync(string instance, string remoteJid, string mediaUrl, string caption);
    bool IsMessageFromBot(string remoteJid, string? messageId);
    Task<InstanceInfoDto?> GetInstanceInfoAsync(string instanceName);
}
