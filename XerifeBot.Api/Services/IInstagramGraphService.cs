namespace XerifeBot.Api.Services;

public interface IInstagramGraphService
{
    /// <summary>
    /// Envia resposta de texto no Direct (POST /me/messages).
    /// </summary>
    Task<bool> SendDirectMessageAsync(string recipientIgsid, string text);

    /// <summary>
    /// Responde diretamente ao comentário do seguidor na postagem (POST /{commentId}/replies).
    /// </summary>
    Task<string?> ReplyToCommentPubliclyAsync(string commentId, string text);

    /// <summary>
    /// Usa o recurso oficial Private Replies da Meta (POST /me/messages passando recipient: { comment_id })
    /// para abrir uma conversa no Direct automaticamente com quem comentou no post!
    /// </summary>
    Task<bool> SendPrivateReplyToCommenterAsync(string commentId, string text);

    /// <summary>
    /// Configura via API (POST /me/messenger_profile) a saudação automática de boas-vindas
    /// para novos seguidores que abrirem o Direct e 3 botões de Ice Breakers.
    /// </summary>
    Task<bool> SetupIceBreakersAndWelcomeAsync();

    /// <summary>
    /// Verifica se a mensagem/eco enviada no Direct partiu do próprio robô automatizado
    /// (para diferenciar de atendente humano usando o app físico do Instagram).
    /// </summary>
    bool IsMessageFromBot(string recipientIgsid, string? messageId);
}
