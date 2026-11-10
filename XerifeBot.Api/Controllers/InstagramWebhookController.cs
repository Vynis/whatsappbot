using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Controllers;

[ApiController]
[Route("api/instagram")]
public class InstagramWebhookController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InstagramWebhookController> _logger;

    public InstagramWebhookController(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<InstagramWebhookController> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 1. GET /api/instagram/webhook (Validação da Meta Graph API)
    /// Recebe hub.mode, hub.verify_token e hub.challenge.
    /// Se o verify_token bater com o appsettings.json, retorna Ok(challenge) em texto puro.
    /// </summary>
    [HttpGet("webhook")]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var configuredToken = _configuration["Instagram:VerifyToken"] ?? "xerife_meta_verify_token_2026";

        if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(verifyToken, configuredToken, StringComparison.Ordinal))
        {
            _logger.LogInformation("✅ Webhook do Instagram validado com sucesso pela Meta. Challenge: {Challenge}", challenge);
            return Content(challenge ?? string.Empty, "text/plain");
        }

        _logger.LogWarning("❌ Falha na validação do webhook do Instagram. Token incorreto ou modo inválido: {VerifyToken}", verifyToken);
        return Forbid();
    }

    /// <summary>
    /// 2. POST /api/instagram/webhook (Recebimento de Eventos da Meta)
    /// Retorna 200 OK imediatamente para a Meta e despacha o processamento em background com Task.Run.
    /// </summary>
    [HttpPost("webhook")]
    public IActionResult ReceiveWebhook([FromBody] InstagramWebhookPayload payload)
    {
        if (payload?.Entry == null || payload.Entry.Count == 0)
        {
            return Ok(new { status = "ignored", reason = "empty_payload" });
        }

        // Retorna HTTP 200 OK imediatamente para cumprir os requisitos de SLA da Meta (< 5s)
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var igService = scope.ServiceProvider.GetRequiredService<IInstagramGraphService>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAiBotService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<InstagramWebhookController>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            try
            {
                await ProcessInstagramPayloadAsync(payload, dbContext, igService, aiService, config, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no processamento em background de eventos do Instagram Webhook.");
            }
        });

        return Ok(new { status = "received" });
    }

    private static async Task ProcessInstagramPayloadAsync(
        InstagramWebhookPayload payload,
        AppDbContext dbContext,
        IInstagramGraphService igService,
        IAiBotService aiService,
        IConfiguration configuration,
        ILogger logger)
    {
        string pageAccountId = configuration["Instagram:AccountId"] ?? string.Empty;

        foreach (var entry in payload.Entry!)
        {
            // =========================================================================
            // Cenário A - Mensagens no Direct (messaging)
            // =========================================================================
            if (entry.Messaging != null && entry.Messaging.Count > 0)
            {
                foreach (var msgEvent in entry.Messaging)
                {
                    await ProcessMessagingEventAsync(msgEvent, dbContext, igService, aiService, pageAccountId, logger);
                }
            }

            // =========================================================================
            // Cenário B - Comentários nas Postagens (changes -> field == "comments")
            // =========================================================================
            if (entry.Changes != null && entry.Changes.Count > 0)
            {
                foreach (var change in entry.Changes)
                {
                    if (string.Equals(change.Field, "comments", StringComparison.OrdinalIgnoreCase) && change.Value != null)
                    {
                        await ProcessCommentChangeAsync(change.Value, dbContext, igService, aiService, pageAccountId, logger);
                    }
                }
            }
        }
    }

    private static async Task ProcessMessagingEventAsync(
        InstagramMessagingEvent msgEvent,
        AppDbContext dbContext,
        IInstagramGraphService igService,
        IAiBotService aiService,
        string pageAccountId,
        ILogger logger)
    {
        bool isEcho = msgEvent.Message?.IsEcho ?? false;
        string? mid = msgEvent.Message?.Mid;
        string? text = msgEvent.Message?.Text;

        // Se for eco enviado pelo bot ou pelo atendente
        if (isEcho)
        {
            string customerIgsid = msgEvent.Recipient?.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(customerIgsid))
            {
                return;
            }

            // Verificar se o eco partiu do nosso próprio bot
            bool isFromBot = igService.IsMessageFromBot(customerIgsid, mid);
            if (!isFromBot)
            {
                // Mensagem enviada manualmente pelo app do Instagram pelo atendente humano!
                logger.LogWarning("🤠 Atendimento Humano Detectado no Instagram Direct! Mensagem enviada pelo celular para IGSID {Igsid}. Pausando Bot por 12 horas.", customerIgsid);

                var session = await dbContext.CustomerSessions.FirstOrDefaultAsync(s => s.RemoteJid == customerIgsid);
                if (session == null)
                {
                    session = new CustomerSession
                    {
                        RemoteJid = customerIgsid,
                        Channel = CommunicationChannel.Instagram,
                        PushName = $"Instagram @{customerIgsid}",
                        LastInteractionAt = DateTime.UtcNow,
                        IsBotPaused = true,
                        BotPausedUntil = DateTime.UtcNow.AddHours(12),
                        PauseReason = "Atendimento manual via app do Instagram"
                    };
                    dbContext.CustomerSessions.Add(session);
                }
                else
                {
                    session.IsBotPaused = true;
                    session.BotPausedUntil = DateTime.UtcNow.AddHours(12);
                    session.PauseReason = "Atendimento manual via app do Instagram";
                    session.LastInteractionAt = DateTime.UtcNow;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    dbContext.MessageLogs.Add(new MessageLog
                    {
                        RemoteJid = customerIgsid,
                        Sender = MessageSender.HumanAgent,
                        Content = text,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await dbContext.SaveChangesAsync();
            }

            return;
        }

        // Mensagem recebida do seguidor/cliente (is_echo == false)
        string igsid = msgEvent.Sender?.Id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(igsid))
        {
            return;
        }

        // Extrai texto da mensagem regular, postback de Ice Breaker ou Quick Reply
        string? customerMessage = msgEvent.Message?.Text 
            ?? msgEvent.Postback?.Title 
            ?? msgEvent.Postback?.Payload 
            ?? msgEvent.Message?.QuickReply?.Payload;

        if (string.IsNullOrWhiteSpace(customerMessage))
        {
            return;
        }

        // Obter ou criar sessão do cliente no canal Instagram
        var customerSession = await dbContext.CustomerSessions.FirstOrDefaultAsync(s => s.RemoteJid == igsid);
        if (customerSession == null)
        {
            customerSession = new CustomerSession
            {
                RemoteJid = igsid,
                Channel = CommunicationChannel.Instagram,
                PushName = $"Instagram @{igsid}",
                LastInteractionAt = DateTime.UtcNow,
                IsBotPaused = false
            };
            dbContext.CustomerSessions.Add(customerSession);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            customerSession.LastInteractionAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        // Registrar mensagem do cliente no log de auditoria
        dbContext.MessageLogs.Add(new MessageLog
        {
            RemoteJid = igsid,
            Sender = MessageSender.Customer,
            Content = customerMessage,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        // Verificar trava mestra global do robô
        var settingsDict = await dbContext.BotSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        bool isBotGloballyActive = !settingsDict.TryGetValue("IsBotActiveGlobally", out var botActiveStr) || 
                                   !string.Equals(botActiveStr, "false", StringComparison.OrdinalIgnoreCase);

        if (!isBotGloballyActive)
        {
            logger.LogInformation("⏸️ O robô está DESATIVADO GLOBALMENTE. Direct do Instagram de {Igsid} registrado sem resposta automática.", igsid);
            return;
        }

        string normalized = customerMessage.Trim().ToLowerInvariant();

        // Checar se o cliente pediu reativação
        if (normalized == "#bot" || normalized == "menu")
        {
            logger.LogInformation("Cliente {Igsid} no Instagram solicitou reativação do bot via comando '{Text}'.", igsid, customerMessage);
            customerSession.IsBotPaused = false;
            customerSession.BotPausedUntil = null;
            customerSession.PauseReason = null;
            await dbContext.SaveChangesAsync();
        }

        // Se estiver em pausa, não responder
        if (customerSession.IsCurrentlyPaused)
        {
            logger.LogInformation("Bot está pausado para Instagram {Igsid} até {PausedUntil}. Mensagem registrada em silêncio.", igsid, customerSession.BotPausedUntil);
            return;
        }

        // Checar se o cliente pediu atendimento humano explicitamente
        if (normalized == "9" || normalized.Contains("humano") || normalized.Contains("atendente"))
        {
            logger.LogInformation("Cliente {Igsid} no Instagram solicitou atendente humano. Pausando bot por 12h.", igsid);
            customerSession.IsBotPaused = true;
            customerSession.BotPausedUntil = DateTime.UtcNow.AddHours(12);
            customerSession.PauseReason = "Cliente solicitou atendimento humano (Instagram)";
            await dbContext.SaveChangesAsync();
        }

        // Tratamento especial para Ice Breaker de Cupom
        if (string.Equals(customerMessage, "ICE_BREAKER_COUPON", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("vempraxerife") ||
            (normalized.Contains("cupom") && normalized.Contains("1ª")))
        {
            string couponReply = "Sensacional! 🤠🎟️ Seu cupom de 1ª locação é VEMPRAXERIFE!\n\n" +
                                 "Ele garante um desconto exclusivo na sua primeira locação de jogos de tabuleiro aqui na Xerife Board Games! " +
                                 "Já tem algum jogo em mente para a jogatina ou quer que eu te mande sugestões para a sua turma?";

            bool sentCoupon = await igService.SendDirectMessageAsync(igsid, couponReply);
            if (sentCoupon)
            {
                dbContext.MessageLogs.Add(new MessageLog
                {
                    RemoteJid = igsid,
                    Sender = MessageSender.Bot,
                    Content = couponReply,
                    CreatedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
            return;
        }

        // Processar mensagem com IA (XerifeAiService)
        string? botReply = await aiService.ProcessAiMessageAsync(customerMessage, igsid, customerSession.PushName, CommunicationChannel.Instagram);

        if (string.IsNullOrWhiteSpace(botReply))
        {
            botReply = "Olá! 🤠🎲 Seja muito bem-vindo à Xerife Board Games no Instagram! Como posso te ajudar hoje a escolher o jogo de tabuleiro ideal para sua jogatina? Se preferir falar direto com um atendente humano da equipe, é só digitar *9*!";
        }

        // Se a resposta da IA contiver a tag [PAUSAR_BOT], remover a tag e gravar a pausa de 12h no MySQL
        bool hasPauseTag = botReply.Contains("[PAUSAR_BOT]", StringComparison.OrdinalIgnoreCase);
        if (hasPauseTag)
        {
            botReply = botReply.Replace("[PAUSAR_BOT]", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        bool sent = await igService.SendDirectMessageAsync(igsid, botReply);
        if (sent)
        {
            dbContext.MessageLogs.Add(new MessageLog
            {
                RemoteJid = igsid,
                Sender = MessageSender.Bot,
                Content = botReply,
                CreatedAt = DateTime.UtcNow
            });

            // Se conteve [PAUSAR_BOT] ou indicou transferência para atendente humano, acionar pausa
            bool indicatesHandover = hasPauseTag ||
                botReply.Contains("atendente humano", StringComparison.OrdinalIgnoreCase) ||
                botReply.Contains("humano vai assumir", StringComparison.OrdinalIgnoreCase) ||
                botReply.Contains("humano assumirá", StringComparison.OrdinalIgnoreCase) ||
                botReply.Contains("atendente da base", StringComparison.OrdinalIgnoreCase) ||
                botReply.Contains("chamando um atendente", StringComparison.OrdinalIgnoreCase) ||
                botReply.Contains("transferindo para", StringComparison.OrdinalIgnoreCase);

            if (indicatesHandover && !customerSession.IsCurrentlyPaused)
            {
                logger.LogInformation("Resposta do bot indicou transferência para atendente humano no Instagram para {Igsid}. Pausando bot por 12h.", igsid);
                customerSession.IsBotPaused = true;
                customerSession.BotPausedUntil = DateTime.UtcNow.AddHours(12);
                customerSession.PauseReason = "Transferido para Atendente Humano (IA Instagram)";
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task ProcessCommentChangeAsync(
        InstagramCommentChangeValue commentValue,
        AppDbContext dbContext,
        IInstagramGraphService igService,
        IAiBotService aiService,
        string pageAccountId,
        ILogger logger)
    {
        string? commentId = commentValue.Id;
        string? commentText = commentValue.Text;
        string username = commentValue.From?.Username ?? string.Empty;
        string userId = commentValue.From?.Id ?? string.Empty;
        string? mediaId = commentValue.Media?.Id;

        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(commentText))
        {
            return;
        }

        // 1. Ignorar comentários feitos pela própria conta @xerifebg ou ID da página
        if (string.Equals(username, "xerifebg", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(pageAccountId) && string.Equals(userId, pageAccountId, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("Ignorando comentário postado pela própria conta da Xerife (@{Username}).", username);
            return;
        }

        // 2. Prevenção de duplicidade: verificar se comentário já foi respondido
        bool alreadyProcessed = await dbContext.InstagramCommentLogs.AnyAsync(c => c.CommentId == commentId);
        if (alreadyProcessed)
        {
            logger.LogInformation("Comentário {CommentId} de @{Username} já foi processado anteriormente. Ignorando.", commentId, username);
            return;
        }

        logger.LogInformation("Novo comentário no post recebido no Instagram de @{Username}: \"{Text}\"", username, commentText);

        // 3. Gerar resposta pública curta, simpática e empolgada com a IA convidando para ver o Direct
        string publicReply = await aiService.GenerateCommentPublicReplyAsync(username, commentText);

        // 4. Responder publicamente no post
        string? replyId = await igService.ReplyToCommentPubliclyAsync(commentId, publicReply);

        // 5. Disparar Private Reply automático abrindo conversa no Direct
        string privateDmText = $"Opa, @{username}! Vi seu comentário lá no nosso post! 🤠🎲 Tá procurando algum jogo específico para o fim de semana ou quer que eu te mande até 3 sugestões certeiras pra sua galera? Ah, se for sua primeira vez com a gente, já separa o cupom VEMPRAXERIFE!";

        bool dmSent = await igService.SendPrivateReplyToCommenterAsync(commentId, privateDmText);

        // 6. Gravar log do comentário no MySQL
        var commentLog = new InstagramCommentLog
        {
            CommentId = commentId,
            MediaId = mediaId,
            Username = username,
            CommentText = commentText,
            PublicReplyText = publicReply,
            PrivateDmSent = dmSent,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.InstagramCommentLogs.Add(commentLog);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Comentário {CommentId} de @{Username} processado com sucesso! Resposta pública enviada: {ReplyOk}, Direct enviado: {DmOk}",
            commentId, username, replyId != null, dmSent);
    }
}
