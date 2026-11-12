using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
[EnableCors("AllowAngularDevClient")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IEvolutionApiService _evolutionApiService;
    private readonly ICatalogImportService _catalogImportService;
    private readonly IAiBotService _aiBotService;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext dbContext,
        IEvolutionApiService evolutionApiService,
        ICatalogImportService catalogImportService,
        IAiBotService aiBotService,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _evolutionApiService = evolutionApiService;
        _catalogImportService = catalogImportService;
        _aiBotService = aiBotService;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("whatsapp-status")]
    public async Task<ActionResult<WhatsAppStatusDto>> GetWhatsAppStatus()
    {
        _logger.LogInformation("Recebida requisição GET /api/admin/whatsapp-status");
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue 
            ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) 
            : null;

        string instanceName = tenant?.WhatsAppInstanceName 
            ?? _configuration["EvolutionApi:InstanceName"] 
            ?? "XerifeAtendimento";

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
            ProfileName = info?.ProfileName ?? tenant?.Name ?? "Xerife Board Games"
        });
    }

    [HttpPost("whatsapp/connect")]
    public async Task<ActionResult<WhatsAppConnectResultDto>> ConnectWhatsApp()
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/whatsapp/connect");
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue 
            ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) 
            : null;

        if (tenant == null)
        {
            return Unauthorized(new { message = "Tenant não identificado." });
        }

        if (string.IsNullOrWhiteSpace(tenant.WhatsAppInstanceName))
        {
            tenant.WhatsAppInstanceName = $"evo_{tenant.Slug}_{Guid.NewGuid().ToString("N")[..6]}";
            tenant.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        var result = await _evolutionApiService.ConnectInstanceAsync(tenant.WhatsAppInstanceName);
        return Ok(result);
    }

    [HttpPost("whatsapp/disconnect")]
    public async Task<ActionResult<WhatsAppActionResponseDto>> DisconnectWhatsApp()
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/whatsapp/disconnect");
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue 
            ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) 
            : null;

        if (tenant == null || string.IsNullOrWhiteSpace(tenant.WhatsAppInstanceName))
        {
            return BadRequest(new WhatsAppActionResponseDto
            {
                Success = false,
                Message = "Nenhuma instância WhatsApp associada a esta conta."
            });
        }

        bool success = await _evolutionApiService.LogoutInstanceAsync(tenant.WhatsAppInstanceName);
        return Ok(new WhatsAppActionResponseDto
        {
            Success = success,
            Message = success 
                ? "Sessão do WhatsApp desconectada com sucesso!" 
                : "Falha ao desconectar sessão na Evolution API.",
            Status = "close"
        });
    }

    [HttpPost("whatsapp/restart")]
    public async Task<ActionResult<WhatsAppActionResponseDto>> RestartWhatsApp()
    {
        _logger.LogInformation("Recebida requisição POST /api/admin/whatsapp/restart");
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue 
            ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) 
            : null;

        if (tenant == null || string.IsNullOrWhiteSpace(tenant.WhatsAppInstanceName))
        {
            return BadRequest(new WhatsAppActionResponseDto
            {
                Success = false,
                Message = "Nenhuma instância WhatsApp associada a esta conta."
            });
        }

        bool success = await _evolutionApiService.RestartInstanceAsync(tenant.WhatsAppInstanceName);
        return Ok(new WhatsAppActionResponseDto
        {
            Success = success,
            Message = success 
                ? "Instância do WhatsApp reiniciada com sucesso!" 
                : "Falha ao reiniciar instância na Evolution API."
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
                string instanceName = _configuration["EvolutionApi:InstanceName"] ?? "XerifeAtendimento";
                bool sent = await _evolutionApiService.SendTextAsync(instanceName, session.RemoteJid, goodbyeMessage);

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
            SalePrice = g.SalePrice,
            AquisitionPrice = g.AquisitionPrice,
            LinkLudopedia = g.LinkLudopedia,
            CopyName = g.CopyName,
            Slug = g.Slug,
            Status = g.Status,
            Comments = g.Comments,
            IsAvailable = g.IsAvailable
        });

        return Ok(result);
    }

    [HttpPost("games")]
    public async Task<ActionResult<BoardGameDto>> CreateGame([FromBody] CreateOrUpdateBoardGameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "O nome do jogo é obrigatório." });
        }

        var game = new BoardGame
        {
            TenantId = _tenantContext.CurrentTenantId ?? Guid.Empty,
            Name = request.Name.Trim(),
            Category = !string.IsNullOrWhiteSpace(request.Category) ? request.Category.Trim() : "Geral",
            MinPlayers = request.MinPlayers > 0 ? request.MinPlayers : 2,
            MaxPlayers = request.MaxPlayers >= request.MinPlayers ? request.MaxPlayers : 4,
            RentalPrice = request.RentalPrice,
            SalePrice = request.SalePrice,
            AquisitionPrice = request.AquisitionPrice,
            LinkLudopedia = request.LinkLudopedia?.Trim(),
            CopyName = request.CopyName?.Trim(),
            Comments = request.Comments?.Trim(),
            IsAvailable = request.IsAvailable,
            Status = request.IsAvailable ? "available" : "rented"
        };

        _dbContext.BoardGames.Add(game);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Novo jogo '{GameName}' cadastrado com sucesso para o tenant.", game.Name);

        return Ok(new BoardGameDto
        {
            Id = game.Id,
            Name = game.Name,
            Category = game.Category,
            MinPlayers = game.MinPlayers,
            MaxPlayers = game.MaxPlayers,
            RentalPrice = game.RentalPrice,
            SalePrice = game.SalePrice,
            AquisitionPrice = game.AquisitionPrice,
            LinkLudopedia = game.LinkLudopedia,
            CopyName = game.CopyName,
            Slug = game.Slug,
            Status = game.Status,
            Comments = game.Comments,
            IsAvailable = game.IsAvailable
        });
    }

    [HttpPut("games/{id:int}")]
    public async Task<ActionResult<BoardGameDto>> UpdateGame(int id, [FromBody] CreateOrUpdateBoardGameRequest request)
    {
        var game = await _dbContext.BoardGames.FindAsync(id);
        if (game == null)
        {
            return NotFound(new { message = $"Jogo com ID {id} não encontrado." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
            game.Name = request.Name.Trim();

        if (!string.IsNullOrWhiteSpace(request.Category))
            game.Category = request.Category.Trim();

        if (request.MinPlayers > 0)
            game.MinPlayers = request.MinPlayers;

        if (request.MaxPlayers > 0)
            game.MaxPlayers = request.MaxPlayers;

        game.RentalPrice = request.RentalPrice;
        game.SalePrice = request.SalePrice;
        game.AquisitionPrice = request.AquisitionPrice;
        game.LinkLudopedia = request.LinkLudopedia?.Trim();
        game.CopyName = request.CopyName?.Trim();
        game.Comments = request.Comments?.Trim();
        game.IsAvailable = request.IsAvailable;
        game.Status = request.IsAvailable ? "available" : "rented";

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Jogo '{GameName}' atualizado com sucesso.", game.Name);

        return Ok(new BoardGameDto
        {
            Id = game.Id,
            Name = game.Name,
            Category = game.Category,
            MinPlayers = game.MinPlayers,
            MaxPlayers = game.MaxPlayers,
            RentalPrice = game.RentalPrice,
            SalePrice = game.SalePrice,
            AquisitionPrice = game.AquisitionPrice,
            LinkLudopedia = game.LinkLudopedia,
            CopyName = game.CopyName,
            Slug = game.Slug,
            Status = game.Status,
            Comments = game.Comments,
            IsAvailable = game.IsAvailable
        });
    }

    [HttpDelete("games/{id:int}")]
    public async Task<ActionResult<object>> DeleteGame(int id)
    {
        var game = await _dbContext.BoardGames.FindAsync(id);
        if (game == null)
        {
            return NotFound(new { message = $"Jogo com ID {id} não encontrado." });
        }

        _dbContext.BoardGames.Remove(game);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Jogo '{GameName}' (ID {Id}) removido do catálogo com sucesso.", game.Name, id);
        return Ok(new { success = true, message = $"Jogo '{game.Name}' excluído com sucesso!" });
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
        game.Status = game.IsAvailable ? "available" : "rented";
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
            SalePrice = game.SalePrice,
            AquisitionPrice = game.AquisitionPrice,
            LinkLudopedia = game.LinkLudopedia,
            CopyName = game.CopyName,
            Slug = game.Slug,
            Status = game.Status,
            Comments = game.Comments,
            IsAvailable = game.IsAvailable
        });
    }

    [HttpGet("profile")]
    public async Task<ActionResult<TenantProfileDto>> GetProfile()
    {
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) : null;
        if (tenant == null)
        {
            return NotFound(new { message = "Empresa não localizada." });
        }

        var settings = await _dbContext.TenantSettings.FindAsync(tenant.Id);

        return Ok(new TenantProfileDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            BusinessType = tenant.BusinessType,
            CityOrRegion = settings?.CityOrRegion,
            WebsiteUrl = settings?.WebsiteUrl,
            Plan = tenant.Plan,
            WhatsAppInstanceName = tenant.WhatsAppInstanceName
        });
    }

    [HttpPut("profile")]
    public async Task<ActionResult<TenantProfileDto>> UpdateProfile([FromBody] UpdateTenantProfileRequest request)
    {
        var currentTenantId = _tenantContext.CurrentTenantId;
        var tenant = currentTenantId.HasValue ? await _dbContext.Tenants.FindAsync(currentTenantId.Value) : null;
        if (tenant == null)
        {
            return NotFound(new { message = "Empresa não localizada." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
            tenant.Name = request.Name.Trim();

        if (request.BusinessType.HasValue)
            tenant.BusinessType = request.BusinessType.Value;

        tenant.UpdatedAt = DateTime.UtcNow;

        var settings = await _dbContext.TenantSettings.FindAsync(tenant.Id);
        if (settings == null)
        {
            settings = new TenantSettings { TenantId = tenant.Id };
            _dbContext.TenantSettings.Add(settings);
        }

        if (request.CityOrRegion != null)
            settings.CityOrRegion = request.CityOrRegion.Trim();

        if (request.WebsiteUrl != null)
            settings.WebsiteUrl = request.WebsiteUrl.Trim();

        settings.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Perfil da empresa '{TenantName}' atualizado com sucesso.", tenant.Name);

        return Ok(new TenantProfileDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            BusinessType = tenant.BusinessType,
            CityOrRegion = settings.CityOrRegion,
            WebsiteUrl = settings.WebsiteUrl,
            Plan = tenant.Plan,
            WhatsAppInstanceName = tenant.WhatsAppInstanceName
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
        var currentTenantId = _tenantContext.CurrentTenantId;
        if (currentTenantId.HasValue)
        {
            var tenantSettings = await _dbContext.TenantSettings.FindAsync(currentTenantId.Value);
            if (tenantSettings != null)
            {
                return Ok(new BotSettingsDto
                {
                    IsBotActiveGlobally = tenantSettings.IsBotActiveGlobally,
                    RejoyCatalogUrl = tenantSettings.WebsiteUrl ?? "https://xerifebg.rejoy.com.br/search",
                    IsAiEnabled = tenantSettings.IsAiEnabled,
                    AiProvider = tenantSettings.AiProvider,
                    AiApiKey = tenantSettings.AiApiKey ?? string.Empty,
                    AiModel = tenantSettings.AiModel,
                    AiSystemPrompt = tenantSettings.AiSystemPrompt ?? AiBotService.GetDefaultSystemPrompt(),
                    LastSyncAt = tenantSettings.LastCatalogSyncAt,
                    TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
                });
            }
        }

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
        var currentTenantId = _tenantContext.CurrentTenantId;
        if (currentTenantId.HasValue)
        {
            var tenantSettings = await _dbContext.TenantSettings.FindAsync(currentTenantId.Value);
            if (tenantSettings != null)
            {
                tenantSettings.IsBotActiveGlobally = !tenantSettings.IsBotActiveGlobally;
                tenantSettings.UpdatedAt = DateTime.UtcNow;

                // Manter BotSettings legado sincronizado
                var legacySetting = await _dbContext.BotSettings.FindAsync("IsBotActiveGlobally");
                if (legacySetting != null)
                {
                    legacySetting.Value = tenantSettings.IsBotActiveGlobally.ToString().ToLower();
                    legacySetting.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.BotSettings.Add(new BotSetting
                    {
                        Key = "IsBotActiveGlobally",
                        Value = tenantSettings.IsBotActiveGlobally.ToString().ToLower(),
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Estado global do robô do tenant alterado para: {State}", tenantSettings.IsBotActiveGlobally ? "ATIVO" : "DESATIVADO");
                return await GetSettings();
            }
        }

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
        var currentTenantId = _tenantContext.CurrentTenantId;
        if (currentTenantId.HasValue)
        {
            var tenantSettings = await _dbContext.TenantSettings.FindAsync(currentTenantId.Value);
            if (tenantSettings == null)
            {
                tenantSettings = new TenantSettings { TenantId = currentTenantId.Value };
                _dbContext.TenantSettings.Add(tenantSettings);
            }

            if (request.IsBotActiveGlobally.HasValue)
            {
                tenantSettings.IsBotActiveGlobally = request.IsBotActiveGlobally.Value;
                var legacySetting = await _dbContext.BotSettings.FindAsync("IsBotActiveGlobally");
                if (legacySetting != null)
                {
                    legacySetting.Value = request.IsBotActiveGlobally.Value.ToString().ToLower();
                    legacySetting.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.BotSettings.Add(new BotSetting
                    {
                        Key = "IsBotActiveGlobally",
                        Value = request.IsBotActiveGlobally.Value.ToString().ToLower(),
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            if (request.RejoyCatalogUrl != null)
                tenantSettings.WebsiteUrl = request.RejoyCatalogUrl.Trim();

            if (request.IsAiEnabled.HasValue)
                tenantSettings.IsAiEnabled = request.IsAiEnabled.Value;

            if (request.AiProvider != null)
                tenantSettings.AiProvider = request.AiProvider.Trim().ToLower();

            if (request.AiApiKey != null)
                tenantSettings.AiApiKey = request.AiApiKey.Trim();

            if (request.AiModel != null)
                tenantSettings.AiModel = request.AiModel.Trim();

            if (request.AiSystemPrompt != null)
                tenantSettings.AiSystemPrompt = request.AiSystemPrompt;

            tenantSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("TenantSettings atualizadas com sucesso para tenant {TenantId}!", currentTenantId.Value);
            return await GetSettings();
        }

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
