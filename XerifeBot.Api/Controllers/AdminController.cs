using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Controllers;

[ApiController]
[Route("api/admin")]
[EnableCors("AllowAngularDevClient")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IEvolutionApiService _evolutionApiService;
    private readonly IInstagramGraphService _instagramGraphService;
    private readonly ICatalogImportService _catalogImportService;
    private readonly IAiBotService _aiBotService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext dbContext,
        IEvolutionApiService evolutionApiService,
        IInstagramGraphService instagramGraphService,
        ICatalogImportService catalogImportService,
        IAiBotService aiBotService,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _evolutionApiService = evolutionApiService;
        _instagramGraphService = instagramGraphService;
        _catalogImportService = catalogImportService;
        _aiBotService = aiBotService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("whatsapp-status")]
    public async Task<ActionResult<WhatsAppStatusDto>> GetWhatsAppStatus()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/whatsapp-status");
        string instanceName = _configuration["EvolutionApi:InstanceName"] ?? "XerifeAtendimento";
        var info = await _evolutionApiService.GetInstanceInfoAsync(instanceName);

        string rawOwner = info?.OwnerJid ?? string.Empty;
        string formatted = !string.IsNullOrWhiteSpace(rawOwner) ? FormatWhatsAppPhone(rawOwner) : string.Empty;
        bool isConnected = string.Equals(info?.Status, "open", StringComparison.OrdinalIgnoreCase);

        return Ok(new WhatsAppStatusDto
        {
            InstanceName = instanceName,
            Status = info?.Status ?? "desconectado",
            IsConnected = isConnected,
            OwnerJid = rawOwner,
            FormattedPhone = formatted,
            ProfileName = info?.ProfileName ?? "Xerife Board Games"
        });
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<CustomerSessionDto>>> GetSessions()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/sessions");
        var sessions = await _dbContext.CustomerSessions
            .OrderByDescending(s => s.LastInteractionAt)
            .ToListAsync();

        var result = sessions.Select(s => new CustomerSessionDto
        {
            Id = s.Id,
            RemoteJid = s.RemoteJid,
            Channel = s.Channel,
            PushName = s.PushName ?? (s.Channel == CommunicationChannel.Instagram ? $"@{s.RemoteJid}" : "Desconhecido"),
            FormattedPhone = s.Channel == CommunicationChannel.Instagram 
                ? (s.RemoteJid.StartsWith("@") ? s.RemoteJid : $"@{s.RemoteJid}")
                : FormatWhatsAppPhone(s.RemoteJid),
            IsBotPaused = s.IsBotPaused,
            BotPausedUntil = s.BotPausedUntil,
            PauseReason = s.PauseReason,
            LastInteractionAt = s.LastInteractionAt,
            IsCurrentlyPaused = s.IsCurrentlyPaused
        });

        return Ok(result);
    }

    [HttpPost("sessions/{id:int}/toggle-pause")]
    public async Task<ActionResult<CustomerSessionDto>> TogglePause(int id, [FromBody] TogglePauseRequest? request)
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/sessions/{Id}/toggle-pause", id);
        var session = await _dbContext.CustomerSessions.FindAsync(id);
        if (session == null)
        {
            return NotFound(new { message = $"Sessão com ID {id} não encontrada." });
        }

        if (session.IsCurrentlyPaused)
        {
            session.IsBotPaused = false;
            session.BotPausedUntil = null;
            session.PauseReason = null;
            _logger.LogInformation("Bot reativado manualmente pelo painel para o cliente {RemoteJid} ({Channel})", session.RemoteJid, session.Channel);
        }
        else
        {
            int hours = request?.DurationHours ?? 12;
            session.IsBotPaused = true;
            session.BotPausedUntil = DateTime.UtcNow.AddHours(hours);
            session.PauseReason = !string.IsNullOrWhiteSpace(request?.Reason)
                ? request.Reason
                : "Pausado manualmente pelo Painel Admin";
            _logger.LogInformation("Bot pausado manualmente pelo painel para o cliente {RemoteJid} ({Channel}) até {PausedUntil}", session.RemoteJid, session.Channel, session.BotPausedUntil);
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new CustomerSessionDto
        {
            Id = session.Id,
            RemoteJid = session.RemoteJid,
            Channel = session.Channel,
            PushName = session.PushName ?? (session.Channel == CommunicationChannel.Instagram ? $"@{session.RemoteJid}" : "Desconhecido"),
            FormattedPhone = session.Channel == CommunicationChannel.Instagram 
                ? (session.RemoteJid.StartsWith("@") ? session.RemoteJid : $"@{session.RemoteJid}")
                : FormatWhatsAppPhone(session.RemoteJid),
            IsBotPaused = session.IsBotPaused,
            BotPausedUntil = session.BotPausedUntil,
            PauseReason = session.PauseReason,
            LastInteractionAt = session.LastInteractionAt,
            IsCurrentlyPaused = session.IsCurrentlyPaused
        });
    }

    [HttpPost("sessions/{id:int}/close")]
    public async Task<ActionResult<CustomerSessionDto>> CloseSession(int id, [FromBody] CloseSessionRequest? request)
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/sessions/{Id}/close", id);
        var session = await _dbContext.CustomerSessions.FindAsync(id);
        if (session == null)
        {
            return NotFound(new { message = $"Sessão com ID {id} não encontrada." });
        }

        // 1. Enviar mensagem de encerramento amigável no canal correspondente se solicitado
        bool sendGoodbye = request?.SendGoodbyeMessage ?? true;
        if (sendGoodbye)
        {
            string goodbyeMessage = !string.IsNullOrWhiteSpace(request?.CustomMessage)
                ? request.CustomMessage
                : "Atendimento finalizado com sucesso! 🤠🎲\n\nSe precisar de mais alguma coisa, tirar dúvidas ou quiser alugar novos jogos no futuro, basta mandar uma mensagem por aqui.\n\nTenha ótimas partidas e até a próxima!";

            try
            {
                bool sent = false;
                if (session.Channel == CommunicationChannel.Instagram)
                {
                    sent = await _instagramGraphService.SendDirectMessageAsync(session.RemoteJid, goodbyeMessage);
                }
                else
                {
                    string instanceName = _configuration["EvolutionApi:InstanceName"] ?? "XerifeAtendimento";
                    sent = await _evolutionApiService.SendTextAsync(instanceName, session.RemoteJid, goodbyeMessage);
                }

                if (sent)
                {
                    _dbContext.MessageLogs.Add(new MessageLog
                    {
                        RemoteJid = session.RemoteJid,
                        Sender = MessageSender.Bot,
                        Content = goodbyeMessage,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar mensagem de despedida para {RemoteJid} ({Channel})", session.RemoteJid, session.Channel);
            }
        }

        // 2. Despausar o robô e finalizar a sessão de atendimento humano
        session.IsBotPaused = false;
        session.BotPausedUntil = null;
        session.PauseReason = null;
        session.LastInteractionAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Atendimento finalizado com sucesso pelo painel para {RemoteJid} ({Channel}).", session.RemoteJid, session.Channel);

        return Ok(new CustomerSessionDto
        {
            Id = session.Id,
            RemoteJid = session.RemoteJid,
            Channel = session.Channel,
            PushName = session.PushName ?? (session.Channel == CommunicationChannel.Instagram ? $"@{session.RemoteJid}" : "Desconhecido"),
            FormattedPhone = session.Channel == CommunicationChannel.Instagram 
                ? (session.RemoteJid.StartsWith("@") ? session.RemoteJid : $"@{session.RemoteJid}")
                : FormatWhatsAppPhone(session.RemoteJid),
            IsBotPaused = session.IsBotPaused,
            BotPausedUntil = session.BotPausedUntil,
            PauseReason = session.PauseReason,
            LastInteractionAt = session.LastInteractionAt,
            IsCurrentlyPaused = session.IsCurrentlyPaused
        });
    }

    [HttpGet("games")]
    public async Task<ActionResult<IEnumerable<BoardGameDto>>> GetGames()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/games");
        var games = await _dbContext.BoardGames
            .OrderBy(g => g.Name)
            .ToListAsync();

        var result = games.Select(g => new BoardGameDto
        {
            Id = g.Id,
            Name = g.Name,
            Category = g.Category,
            MinPlayers = g.MinPlayers,
            MaxPlayers = g.MaxPlayers,
            RentalPrice = g.RentalPrice,
            IsAvailable = g.IsAvailable
        });

        return Ok(result);
    }

    [HttpPut("games/{id:int}/toggle-availability")]
    public async Task<ActionResult<BoardGameDto>> ToggleGameAvailability(int id)
    {
        _logger.LogInformation("Recebida requisição PUT /api/admin/games/{Id}/toggle-availability", id);
        var game = await _dbContext.BoardGames.FindAsync(id);
        if (game == null)
        {
            return NotFound(new { message = $"Jogo com ID {id} não encontrado." });
        }

        game.IsAvailable = !game.IsAvailable;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Disponibilidade do jogo '{GameName}' alterada para {IsAvailable}", game.Name, game.IsAvailable);

        return Ok(new BoardGameDto
        {
            Id = game.Id,
            Name = game.Name,
            Category = game.Category,
            MinPlayers = game.MinPlayers,
            MaxPlayers = game.MaxPlayers,
            RentalPrice = game.RentalPrice,
            IsAvailable = game.IsAvailable
        });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetDashboardStats()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/stats");
        var now = DateTime.UtcNow;
        var totalSessions = await _dbContext.CustomerSessions.CountAsync();
        var pausedSessions = await _dbContext.CustomerSessions
            .CountAsync(s => s.IsBotPaused && s.BotPausedUntil.HasValue && s.BotPausedUntil.Value > now);
        var activeBotSessions = totalSessions - pausedSessions;

        var totalGames = await _dbContext.BoardGames.CountAsync();
        var availableGames = await _dbContext.BoardGames.CountAsync(g => g.IsAvailable);

        return Ok(new
        {
            totalSessions,
            activeBotSessions,
            pausedSessions,
            totalGames,
            availableGames,
            rentedGames = totalGames - availableGames
        });
    }

    // =========================================================================
    // Endpoints de Configuração e IA
    // =========================================================================

    [HttpGet("settings")]
    public async Task<ActionResult<BotSettingsDto>> GetSettings()
    {
        var settingsDict = await _dbContext.BotSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        string GetVal(string key, string def) =>
            settingsDict.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : def;

        bool.TryParse(GetVal("IsBotActiveGlobally", "true"), out bool isBotActiveGlobally);
        bool.TryParse(GetVal("IsAiEnabled", "false"), out bool isAiEnabled);
        DateTime? lastSync = null;
        if (settingsDict.TryGetValue("LastCatalogSyncAt", out var syncStr) && DateTime.TryParse(syncStr, out var parsedDate))
        {
            lastSync = parsedDate;
        }

        return Ok(new BotSettingsDto
        {
            IsBotActiveGlobally = isBotActiveGlobally,
            RejoyCatalogUrl = GetVal("RejoyCatalogUrl", "https://xerifebg.rejoy.com.br/search"),
            IsAiEnabled = isAiEnabled,
            AiProvider = GetVal("AiProvider", "gemini"),
            AiApiKey = GetVal("AiApiKey", ""),
            AiModel = GetVal("AiModel", "gemini-flash-lite-latest"),
            AiSystemPrompt = GetVal("AiSystemPrompt", AiBotService.GetDefaultSystemPrompt()),
            LastSyncAt = lastSync,
            TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
        });
    }

    [HttpPost("toggle-global-bot")]
    public async Task<ActionResult<BotSettingsDto>> ToggleGlobalBot()
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/toggle-global-bot");
        var setting = await _dbContext.BotSettings.FindAsync("IsBotActiveGlobally");
        bool current = true;
        if (setting != null && bool.TryParse(setting.Value, out bool parsed))
        {
            current = parsed;
        }

        bool newState = !current;
        if (setting == null)
        {
            _dbContext.BotSettings.Add(new BotSetting
            {
                Key = "IsBotActiveGlobally",
                Value = newState.ToString().ToLower(),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = newState.ToString().ToLower();
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Estado global do robô alterado para: {State}", newState ? "ATIVO" : "DESATIVADO");
        return await GetSettings();
    }

    [HttpPut("settings")]
    public async Task<ActionResult<BotSettingsDto>> UpdateSettings([FromBody] UpdateBotSettingsRequest request)
    {
        async Task SetSettingAsync(string key, string? value)
        {
            if (value == null) return;
            var setting = await _dbContext.BotSettings.FindAsync(key);
            if (setting == null)
            {
                _dbContext.BotSettings.Add(new BotSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            }
            else
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (request.IsBotActiveGlobally.HasValue)
            await SetSettingAsync("IsBotActiveGlobally", request.IsBotActiveGlobally.Value.ToString().ToLower());

        if (request.RejoyCatalogUrl != null)
            await SetSettingAsync("RejoyCatalogUrl", request.RejoyCatalogUrl.Trim());

        if (request.IsAiEnabled.HasValue)
            await SetSettingAsync("IsAiEnabled", request.IsAiEnabled.Value.ToString().ToLower());

        if (request.AiProvider != null)
            await SetSettingAsync("AiProvider", request.AiProvider.Trim().ToLower());

        if (request.AiApiKey != null)
            await SetSettingAsync("AiApiKey", request.AiApiKey.Trim());

        if (request.AiModel != null)
            await SetSettingAsync("AiModel", request.AiModel.Trim());

        if (request.AiSystemPrompt != null)
            await SetSettingAsync("AiSystemPrompt", request.AiSystemPrompt);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Configurações do Bot atualizadas com sucesso!");
        return await GetSettings();
    }

    // =========================================================================
    // Endpoints de Importação e Gestão Manual do Catálogo
    // =========================================================================

    [HttpPost("games/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadCatalogResultDto>> UploadCatalog(
        IFormFile file, 
        [FromQuery] bool clearExisting = true)
    {
        _logger.LogInformation("Recebido arquivo de upload de catálogo: {FileName} ({Length} bytes)", file?.FileName, file?.Length);
        if (file == null || file.Length == 0)
        {
            return BadRequest(new UploadCatalogResultDto
            {
                Success = false,
                Message = "Por favor, selecione um arquivo CSV para enviar."
            });
        }

        var result = await _catalogImportService.ImportCatalogAsync(file, clearExisting);
        return Ok(result);
    }

    [HttpPost("games/import-text")]
    public async Task<ActionResult<UploadCatalogResultDto>> ImportCatalogText(
        [FromBody] ImportCsvTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.CsvContent))
        {
            return BadRequest(new UploadCatalogResultDto
            {
                Success = false,
                Message = "Conteúdo CSV não fornecido."
            });
        }

        var result = await _catalogImportService.ImportCsvTextAsync(request.CsvContent, request.ClearExisting);
        return Ok(result);
    }

    [HttpPost("games/clear")]
    public async Task<ActionResult<object>> ClearGames()
    {
        bool cleared = await _catalogImportService.ClearCatalogAsync();
        return Ok(new
        {
            success = cleared,
            message = cleared ? "Catálogo zerado com sucesso!" : "Falha ao zerar catálogo."
        });
    }

    [HttpPost("ai/test")]
    public async Task<ActionResult<TestAiResponse>> TestAi([FromBody] TestAiRequest request)
    {
        string apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var keySetting = await _dbContext.BotSettings.FindAsync("AiApiKey");
            apiKey = keySetting?.Value ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Ok(new TestAiResponse
            {
                Success = false,
                Error = "Chave de API (API Key) não fornecida nem configurada."
            });
        }

        var result = await _aiBotService.TestConnectionAsync(request.Provider, apiKey, request.Model, request.Message);
        return Ok(result);
    }

    [HttpGet("instagram/comments")]
    public async Task<ActionResult<IEnumerable<InstagramCommentLogDto>>> GetInstagramComments()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/instagram/comments");
        var comments = await _dbContext.InstagramCommentLogs
            .OrderByDescending(c => c.CreatedAt)
            .Take(100)
            .Select(c => new InstagramCommentLogDto
            {
                Id = c.Id,
                CommentId = c.CommentId,
                MediaId = c.MediaId,
                Username = c.Username,
                CommentText = c.CommentText,
                PublicReplyText = c.PublicReplyText,
                PrivateDmSent = c.PrivateDmSent,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        return Ok(comments);
    }

    [HttpPost("instagram/setup-icebreakers")]
    public async Task<ActionResult<SetupIceBreakersResultDto>> SetupIceBreakers()
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/instagram/setup-icebreakers");
        bool success = await _instagramGraphService.SetupIceBreakersAndWelcomeAsync();
        return Ok(new SetupIceBreakersResultDto
        {
            Success = success,
            Message = success 
                ? "Ice Breakers e Saudação de Boas-Vindas configurados com sucesso na Meta Instagram Graph API!" 
                : "Falha ao configurar Ice Breakers. Verifique o Access Token e Permissões da Meta no appsettings.json."
        });
    }

    private static string FormatWhatsAppPhone(string remoteJid)
    {
        if (string.IsNullOrWhiteSpace(remoteJid))
            return string.Empty;

        string digits = Regex.Replace(remoteJid.Split('@')[0], @"\D", "");

        if (digits.StartsWith("55") && digits.Length >= 12)
        {
            string ddd = digits.Substring(2, 2);
            string number = digits.Substring(4);
            if (number.Length == 9)
            {
                return $"+55 ({ddd}) {number.Substring(0, 5)}-{number.Substring(5)}";
            }
            if (number.Length == 8)
            {
                return $"+55 ({ddd}) {number.Substring(0, 4)}-{number.Substring(4)}";
            }
        }

        return remoteJid.Split('@')[0];
    }
}
