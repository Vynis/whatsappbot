using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace XerifeBot.Api.Services;

public class InstagramGraphService : IInstagramGraphService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<InstagramGraphService> _logger;
    private readonly string _accessToken;
    private readonly string _accountId;
    private readonly string _baseUrl;

    public InstagramGraphService(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<InstagramGraphService> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;

        _baseUrl = configuration["Instagram:BaseUrl"] ?? "https://graph.instagram.com/v21.0/";
        _accessToken = configuration["Instagram:AccessToken"] ?? string.Empty;
        _accountId = configuration["Instagram:AccountId"] ?? string.Empty;

        // Configurar endereço base
        if (!_baseUrl.EndsWith("/")) _baseUrl += "/";
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>
    /// Envia resposta de texto no Direct (POST /me/messages).
    /// </summary>
    public async Task<bool> SendDirectMessageAsync(string recipientIgsid, string text)
    {
        if (string.IsNullOrWhiteSpace(recipientIgsid) || string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Tentativa de enviar Direct no Instagram com destinatário ou texto vazio.");
            return false;
        }

        try
        {
            // Marca na memória janela de envio para evitar falso-positivo de atendimento humano no eco
            string windowKey = $"ig_bot_sent_window_{recipientIgsid}";
            _memoryCache.Set(windowKey, true, TimeSpan.FromSeconds(30));

            var payload = new
            {
                recipient = new { id = recipientIgsid },
                message = new { text }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "me/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("Enviando Direct no Instagram para IGSID {Igsid}...", recipientIgsid);
            var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao enviar Direct no Instagram ({StatusCode}): {Body}", response.StatusCode, responseBody);
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("message_id", out var msgIdProp))
                {
                    string? msgId = msgIdProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msgId))
                    {
                        _memoryCache.Set($"ig_bot_msg_id_{msgId}", true, TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível extrair message_id do retorno do Instagram Direct.");
            }

            _logger.LogInformation("Direct enviado com sucesso no Instagram para {Igsid}!", recipientIgsid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao disparar SendDirectMessageAsync para {Igsid}", recipientIgsid);
            return false;
        }
    }

    /// <summary>
    /// Responde diretamente ao comentário do seguidor na postagem (POST /{commentId}/replies).
    /// </summary>
    public async Task<string?> ReplyToCommentPubliclyAsync(string commentId, string text)
    {
        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Tentativa de responder comentário no Instagram com commentId ou texto vazio.");
            return null;
        }

        try
        {
            var payload = new { message = text };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{commentId}/replies")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("Respondendo publicamente comentário {CommentId} no Instagram...", commentId);
            var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao responder comentário no Instagram ({StatusCode}): {Body}", response.StatusCode, responseBody);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                string? replyId = idProp.GetString();
                _logger.LogInformation("Comentário respondido com sucesso no Instagram. Reply ID: {ReplyId}", replyId);
                return replyId;
            }

            return "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao disparar ReplyToCommentPubliclyAsync para comentário {CommentId}", commentId);
            return null;
        }
    }

    /// <summary>
    /// Usa o recurso oficial Private Replies da Meta (POST /me/messages passando recipient: { comment_id })
    /// para abrir uma conversa no Direct automaticamente com quem comentou no post!
    /// </summary>
    public async Task<bool> SendPrivateReplyToCommenterAsync(string commentId, string text)
    {
        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Tentativa de Private Reply no Instagram com commentId ou texto vazio.");
            return false;
        }

        try
        {
            var payload = new
            {
                recipient = new { comment_id = commentId },
                message = new { text }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "me/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("Disparando Private Reply no Direct do Instagram para autor do comentário {CommentId}...", commentId);
            var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao enviar Private Reply no Instagram ({StatusCode}): {Body}", response.StatusCode, responseBody);
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("message_id", out var msgIdProp))
                {
                    string? msgId = msgIdProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msgId))
                    {
                        _memoryCache.Set($"ig_bot_msg_id_{msgId}", true, TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível extrair message_id do Private Reply no Instagram.");
            }

            _logger.LogInformation("Private Reply enviado com sucesso no Direct para comentário {CommentId}!", commentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao disparar SendPrivateReplyToCommenterAsync para comentário {CommentId}", commentId);
            return false;
        }
    }

    /// <summary>
    /// Configura via API (POST /me/messenger_profile) a saudação automática de boas-vindas
    /// para novos seguidores que abrirem o Direct e 3 botões de Ice Breakers.
    /// </summary>
    public async Task<bool> SetupIceBreakersAndWelcomeAsync()
    {
        try
        {
            var payload = new
            {
                greeting = new[]
                {
                    new
                    {
                        locale = "default",
                        text = "Boas-vindas à Xerife Board Games (@xerifebg)! 🤠🎲 Escolha uma das opções abaixo para começarmos ou mande sua mensagem:"
                    }
                },
                ice_breakers = new[]
                {
                    new
                    {
                        question = "🎲 Quero sugestões de jogos!",
                        payload = "ICE_BREAKER_SUGGESTIONS"
                    },
                    new
                    {
                        question = "🔎 Estou procurando um jogo específico",
                        payload = "ICE_BREAKER_SEARCH"
                    },
                    new
                    {
                        question = "🎟️ Quero meu cupom de 1ª locação (VEMPRAXERIFE)",
                        payload = "ICE_BREAKER_COUPON"
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "me/messenger_profile")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("Configurando Ice Breakers e Boas-Vindas no perfil do Instagram Messenger...");
            var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao configurar Ice Breakers no Instagram ({StatusCode}): {Body}", response.StatusCode, responseBody);
                return false;
            }

            _logger.LogInformation("Ice Breakers e Saudação configurados com sucesso na Meta Instagram Graph API!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao executar SetupIceBreakersAndWelcomeAsync");
            return false;
        }
    }

    public bool IsMessageFromBot(string recipientIgsid, string? messageId)
    {
        if (!string.IsNullOrWhiteSpace(messageId) && _memoryCache.TryGetValue($"ig_bot_msg_id_{messageId}", out _))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(recipientIgsid) && _memoryCache.TryGetValue($"ig_bot_sent_window_{recipientIgsid}", out _))
        {
            return true;
        }

        return false;
    }
}
