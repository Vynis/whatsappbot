using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

public interface IAiBotService
{
    Task<string?> ProcessAiMessageAsync(string customerMessage, string remoteJid, string? pushName, CommunicationChannel channel = CommunicationChannel.WhatsApp);
    Task<string> GenerateCommentPublicReplyAsync(string username, string commentText);
    Task<TestAiResponse> TestConnectionAsync(string provider, string apiKey, string model, string? testMessage);
}

/// <summary>
/// Alias para atender nomenclatura XerifeAiService especificada nos requisitos
/// </summary>
public interface IXerifeAiService : IAiBotService
{
}
