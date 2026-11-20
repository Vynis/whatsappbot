using Kora.Api.Dtos;
using Kora.Api.Models;

namespace Kora.Api.Services;

public interface IAiBotService
{
    Task<string?> ProcessAiMessageAsync(string customerMessage, string remoteJid, string? pushName, CommunicationChannel channel = CommunicationChannel.WhatsApp);
    Task<string> GenerateCommentPublicReplyAsync(string username, string commentText);
    Task<TestAiResponse> TestConnectionAsync(string provider, string apiKey, string model, string? testMessage);
}

public interface IKoraAiService : IAiBotService
{
}

/// <summary>
/// Alias para compatibilidade legada
/// </summary>
public interface IXerifeAiService : IKoraAiService
{
}
