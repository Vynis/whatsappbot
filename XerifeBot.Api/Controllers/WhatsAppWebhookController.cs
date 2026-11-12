using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IServiceScopeFactory scopeFactory,
        ILogger<WhatsAppWebhookController> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public IActionResult ReceiveWebhook([FromBody] EvolutionWebhookDto payload)
    {
        // 0. Evento de atualização de conexão da instância (QR Code lido / conectado / desconectado)
        if (payload != null && !string.IsNullOrWhiteSpace(payload.Event) &&
            payload.Event.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Evolution API: Conexão atualizada para a instância '{Instance}' (Evento: {Event})",
                payload.Instance, payload.Event);
            return Ok(new { status = "connection_update_received" });
        }

        // 1. Retorno Imediato para não bloquear a fila de eventos da Evolution API
        if (payload?.Data?.Key == null)
        {
            return Ok(new { status = "ignored", reason = "empty_payload" });
        }

        string? remoteJid = payload.Data.Key.RemoteJid;
        if (string.IsNullOrWhiteSpace(remoteJid))
        {
            return Ok(new { status = "ignored", reason = "no_remote_jid" });
        }

        // 2. Trava absoluta: Ignorar Grupos (@g.us), Participantes de Grupo, Canais (@newsletter) e Status/Broadcasts
        if (remoteJid.Contains("@g.us", StringComparison.OrdinalIgnoreCase) ||
            remoteJid.Contains("@newsletter", StringComparison.OrdinalIgnoreCase) ||
            remoteJid.Contains("broadcast", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(payload.Data.Key.Participant))
        {
            return Ok(new { status = "ignored", reason = "group_channel_or_broadcast" });
        }

        // 3. Ignorar eventos administrativos de grupo
        if (!string.IsNullOrWhiteSpace(payload.Event) &&
            payload.Event.Contains("GROUP", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { status = "ignored", reason = "group_event" });
        }

        // Processamento em Background utilizando IServiceScopeFactory
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var evolutionApi = scope.ServiceProvider.GetRequiredService<IEvolutionApiService>();
            var botFlow = scope.ServiceProvider.GetRequiredService<IBotFlowService>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAiBotService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<WhatsAppWebhookController>>();

            try
            {
                await ProcessIncomingMessageAsync(payload, dbContext, tenantContext, evolutionApi, botFlow, aiService, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar mensagem do webhook para JID {RemoteJid}", remoteJid);
            }
        });

        return Ok(new { status = "received" });
    }

    private static async Task ProcessIncomingMessageAsync(
        EvolutionWebhookDto payload,
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IEvolutionApiService evolutionApi,
        IBotFlowService botFlow,
        IAiBotService aiService,
        ILogger logger)
    {
        var key = payload.Data!.Key!;
        string remoteJid = key.RemoteJid!;
        string? messageId = key.Id;
        bool fromMe = key.FromMe;
        string? messageText = payload.Data.ExtractText();
        string? pushName = payload.Data.PushName;
        string instance = !string.IsNullOrWhiteSpace(payload.Instance) ? payload.Instance : "XerifeAtendimento";

        // Resolver Tenant pela instância da Evolution API (ignorando query filter de início)
        var tenant = await dbContext.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.WhatsAppInstanceName == instance || t.Slug == instance);

        if (tenant == null)
        {
            var xerifeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            tenant = await dbContext.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == xerifeTenantId);
        }

        if (tenant != null)
        {
            tenantContext.SetTenantId(tenant.Id);
        }
        else
        {
            logger.LogWarning("Nenhum Tenant localizado para a instância WhatsApp '{Instance}'.", instance);
            return;
        }

        // Obter ou criar sessão do cliente
        var session = await dbContext.CustomerSessions.FirstOrDefaultAsync(s => s.RemoteJid == remoteJid);
        if (session == null)
        {
            session = new CustomerSession
            {
                RemoteJid = remoteJid,
                Channel = CommunicationChannel.WhatsApp,
                PushName = pushName,
                LastInteractionAt = DateTime.UtcNow,
                IsBotPaused = false
            };
            dbContext.CustomerSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(pushName))
                session.PushName = pushName;

            session.LastInteractionAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        // =====================================================================
        // 3. Trava Automática de Atendimento Humano (fromMe == true)
        // =====================================================================
        if (fromMe)
        {
            // Checar se foi o próprio bot que enviou a mensagem
            bool isFromBot = evolutionApi.IsMessageFromBot(remoteJid, messageId);

            if (!isFromBot)
            {
                // Se NÃO foi gerada pelo Bot, significa que o atendente humano pegou o celular físico e respondeu!
                logger.LogWarning("🤠 Atendimento Humano Detectado! Atendente enviou mensagem manual pelo celular para {RemoteJid}. Pausando Bot por 12 horas.", remoteJid);

                session.IsBotPaused = true;
                session.BotPausedUntil = DateTime.UtcNow.AddHours(12);
                session.PauseReason = "Atendimento manual via celular";
                await dbContext.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(messageText))
                {
                    dbContext.MessageLogs.Add(new MessageLog
                    {
                        RemoteJid = remoteJid,
                        Sender = MessageSender.HumanAgent,
                        Content = messageText,
                        CreatedAt = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync();
                }
            }

            return;
        }

        // =====================================================================
        // 4. Mensagem do Cliente (fromMe == false)
        // =====================================================================
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return;
        }

        // Registrar mensagem do cliente no log de auditoria
        dbContext.MessageLogs.Add(new MessageLog
        {
            RemoteJid = remoteJid,
            Sender = MessageSender.Customer,
            Content = messageText,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        // =====================================================================
        // Trava Mestra Global: Checar se o robô está ativo para este Tenant
        // =====================================================================
        var tenantSettings = await dbContext.TenantSettings.FindAsync(tenant.Id);
        bool isBotGloballyActive = tenantSettings?.IsBotActiveGlobally ?? true;

        if (!isBotGloballyActive)
        {
            logger.LogInformation("⏸️ O robô da empresa '{TenantName}' está DESATIVADO no painel. Mensagem de {RemoteJid} gravada no histórico sem envio de resposta automática.", tenant.Name, remoteJid);
            return;
        }

        string normalizedText = messageText.Trim().ToLowerInvariant();

        // Checar se o cliente está solicitando explicitamente reativar o robô
        bool isReactivationRequest = normalizedText == "#bot" || normalizedText == "menu";
        if (isReactivationRequest)
        {
            logger.LogInformation("Cliente {RemoteJid} solicitou reativação do bot via comando '{Text}'.", remoteJid, messageText);
            session.IsBotPaused = false;
            session.BotPausedUntil = null;
            session.PauseReason = null;
            await dbContext.SaveChangesAsync();
        }

        // Checar se o bot está pausado para este cliente
        if (session.IsCurrentlyPaused)
        {
            logger.LogInformation("Bot está pausado para {RemoteJid} até {PausedUntil}. Mensagem registrada em silêncio.", remoteJid, session.BotPausedUntil);
            return;
        }

        // Checar se o cliente está solicitando explicitamente atendimento humano
        if (normalizedText == "9" || normalizedText.Contains("humano") || normalizedText.Contains("atendente"))
        {
            logger.LogInformation("Cliente {RemoteJid} solicitou atendente humano. Pausando bot por 12h.", remoteJid);
            session.IsBotPaused = true;
            session.BotPausedUntil = DateTime.UtcNow.AddHours(12);
            session.PauseReason = "Cliente solicitou atendimento humano";
            await dbContext.SaveChangesAsync();
        }

        // Processar fluxo de atendimento do bot:
        // 1. Tentar Inteligência Artificial configurável (Gemini / OpenAI)
        string? botReply = null;
        botReply = await aiService.ProcessAiMessageAsync(messageText, remoteJid, pushName);

        // 2. Se a IA estiver desativada no painel ou retornar nulo, segue o fluxo de menu tradicional
        if (string.IsNullOrWhiteSpace(botReply))
        {
            botReply = await botFlow.ProcessMessageAsync(session, messageText);
        }

        // 3. Se a IA estiver ativada nas configurações mas não gerou resposta (ex: erro de rede/cota), evitar silêncio
        bool isAiConfigured = tenantSettings?.IsAiEnabled ?? false;

        if (isAiConfigured && string.IsNullOrWhiteSpace(botReply))
        {
            botReply = "Opa, tive uma pequena oscilação aqui na conexão no momento! 🤠\n\nPoderia repetir sua mensagem, por favor? Se preferir falar diretamente com um atendente humano agora, é só digitar *9*!";
        }

        if (!string.IsNullOrWhiteSpace(botReply))
        {
            bool hasPauseTag = botReply.Contains("[PAUSAR_BOT]", StringComparison.OrdinalIgnoreCase);
            if (hasPauseTag)
            {
                botReply = botReply.Replace("[PAUSAR_BOT]", "", StringComparison.OrdinalIgnoreCase).Trim();
            }

            bool sent = await evolutionApi.SendTextAsync(instance, remoteJid, botReply);
            if (sent)
            {
                dbContext.MessageLogs.Add(new MessageLog
                {
                    RemoteJid = remoteJid,
                    Sender = MessageSender.Bot,
                    Content = botReply,
                    CreatedAt = DateTime.UtcNow
                });

                // Se a resposta enviada indicou que um atendente humano assumirá o atendimento (ou tag [PAUSAR_BOT]),
                // pausar o robô automaticamente para permitir o atendimento humano sem interrupções.
                bool indicatesHandover = hasPauseTag ||
                    botReply.Contains("atendente humano", StringComparison.OrdinalIgnoreCase) ||
                    botReply.Contains("humano vai assumir", StringComparison.OrdinalIgnoreCase) ||
                    botReply.Contains("humano assumirá", StringComparison.OrdinalIgnoreCase) ||
                    botReply.Contains("atendente da base", StringComparison.OrdinalIgnoreCase) ||
                    botReply.Contains("chamando um atendente", StringComparison.OrdinalIgnoreCase) ||
                    botReply.Contains("transferindo para", StringComparison.OrdinalIgnoreCase);

                if (indicatesHandover && !session.IsCurrentlyPaused)
                {
                    logger.LogInformation("Resposta do bot indicou transferência para atendente humano para {RemoteJid}. Pausando bot por 12h.", remoteJid);
                    session.IsBotPaused = true;
                    session.BotPausedUntil = DateTime.UtcNow.AddHours(12);
                    session.PauseReason = "Transferido para Atendente Humano (IA)";
                }

                await dbContext.SaveChangesAsync();
            }
        }
    }
}
