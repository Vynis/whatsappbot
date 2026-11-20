using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Kora.Api.Data;
using Kora.Api.Dtos;
using Kora.Api.Models;

namespace Kora.Api.Services;

public class AiBotService : IAiBotService, IXerifeAiService
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AiBotService> _logger;

    public AiBotService(
        HttpClient httpClient,
        AppDbContext dbContext,
        ILogger<AiBotService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string?> ProcessAiMessageAsync(string customerMessage, string remoteJid, string? pushName, CommunicationChannel channel = CommunicationChannel.WhatsApp)
    {
        try
        {
            var settings = await GetSettingsAsync();
            if (!settings.IsAiEnabled || string.IsNullOrWhiteSpace(settings.AiApiKey))
            {
                return null; // IA desligada, segue para o menu tradicional
            }

            // 1. Obter catálogo atualizado de jogos disponíveis para injetar no contexto da IA (RAG leve)
            // Filtro específico: status == "available" (ou status nulo com IsAvailable == true)
            var availableGames = await _dbContext.BoardGames
                .Where(g => (g.Status != null && g.Status.ToLower() == "available") || (g.Status == null && g.IsAvailable))
                .OrderBy(g => g.Name)
                .ToListAsync();

            string pattern = !string.IsNullOrWhiteSpace(settings.ProductUrlPattern)
                ? settings.ProductUrlPattern.Trim()
                : (!string.IsNullOrWhiteSpace(settings.RejoyCatalogUrl)
                    ? $"{settings.RejoyCatalogUrl.Split("/search")[0].TrimEnd('/')}/boardgame/{{slug}}"
                    : string.Empty);

            string baseSiteUrl = pattern.Contains("/boardgame", StringComparison.OrdinalIgnoreCase)
                ? pattern.Split(new[] { "/boardgame" }, StringSplitOptions.None)[0].TrimEnd('/')
                : (!string.IsNullOrWhiteSpace(settings.RejoyCatalogUrl)
                    ? settings.RejoyCatalogUrl.Split("/search")[0].TrimEnd('/')
                    : string.Empty);

            var gamesContext = new StringBuilder();
            gamesContext.AppendLine("\n--- ITENS / PRODUTOS DISPONÍVEIS AGORA (CONFIRMADOS EM ESTOQUE) ---");
            foreach (var g in availableGames)
            {
                var sbItem = new StringBuilder();
                sbItem.Append($"• {g.Name}");

                if (settings.IsWhatsAppCatalogLinkEnabled && !string.IsNullOrWhiteSpace(g.Slug) && !string.IsNullOrWhiteSpace(pattern))
                {
                    string gameUrl = pattern.Contains("{slug}", StringComparison.OrdinalIgnoreCase)
                        ? pattern.Replace("{slug}", g.Slug.Trim('/'), StringComparison.OrdinalIgnoreCase)
                        : $"{pattern.TrimEnd('/')}/{g.Slug.Trim('/')}";

                    sbItem.Append($" - Link Oficial (Ver Fotos e Reservar): {gameUrl}");
                }
                else if (!string.IsNullOrWhiteSpace(g.LinkLudopedia))
                {
                    sbItem.Append($" - Link: {g.LinkLudopedia}");
                }

                gamesContext.AppendLine(sbItem.ToString());
            }

            // 2. Obter histórico da conversa recente (últimas 24 horas, até 8 mensagens)
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            var recentLogs = await _dbContext.MessageLogs
                .Where(m => m.RemoteJid == remoteJid && m.CreatedAt >= cutoffTime)
                .OrderByDescending(m => m.CreatedAt)
                .Take(8)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var historyContext = new StringBuilder();
            if (recentLogs.Any())
            {
                historyContext.AppendLine("\n--- HISTÓRICO RECENTE DA CONVERSA (ÚLTIMAS 24H) ---");
                foreach (var log in recentLogs)
                {
                    string sender = log.Sender == MessageSender.Customer ? "Cliente" : "Atendente (Você)";
                    historyContext.AppendLine($"{sender}: {log.Content}");
                }
            }

            // 3. Montar o Prompt do Sistema completo
            string systemPrompt = !string.IsNullOrWhiteSpace(settings.AiSystemPrompt)
                ? settings.AiSystemPrompt
                : GetDefaultSystemPrompt();

            string channelFormatting = channel == CommunicationChannel.Instagram
                ? "• Responda para o Instagram Direct: tom direto e envolvente, parágrafos curtos, emojis descontraídos.\n"
                : "• Use formatação do WhatsApp (*negrito*, quebras de linha limpas).\n";

            string catalogInstruction = settings.IsWhatsAppCatalogLinkEnabled && !string.IsNullOrWhiteSpace(baseSiteUrl)
                ? $"• EXIBIÇÃO VISUAL: Ao recomendar um item ou falar de um produto específico, inclua o link oficial da página dele fornecido na lista (ex: {baseSiteUrl}/boardgame/...). O WhatsApp exibirá automaticamente a pré-visualização (card) com a foto e permitirá ao cliente conferir detalhes e fazer o pedido!\n" +
                  "• ENVIAR ITENS EM MENSAGENS SEPARADAS (MUITO IMPORTANTE): O WhatsApp só gera o card visual com a foto se o link estiver isolado na sua própria mensagem. Por isso, NUNCA junte múltiplos links na mesma frase ou mensagem! Se for recomendar mais de um item, separe cada um usando a tag [MENSAGEM] para que o sistema envie cada item em um balão separado no WhatsApp com seu respectivo card de imagem.\n"
                : "• Caso o cliente solicite fotos ou catálogo, informe cordialmente que o catálogo está disponível ou que um atendente humano pode enviar mais fotos.\n";

            string fullSystemInstruction = $"{systemPrompt}\n{gamesContext}\n{historyContext}\n" +
                "DIRETRIZES DE ATENDIMENTO:\n" +
                catalogInstruction +
                "• Responda em Português do Brasil com tom amigável, acolhedor e profissional como assistente Kora da empresa.\n" +
                channelFormatting +
                "• Se o cliente pedir para falar com atendente ou quiser finalizar a compra/locação, encerre avisando com entusiasmo que um atendente humano assumirá em instantes E inclua a tag [PAUSAR_BOT] no final do texto para que o sistema acione a pausa automática.";

            // 4. Executar chamada para a IA (Gemini ou OpenAI)
            string? reply = null;
            if (string.Equals(settings.AiProvider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                reply = await CallOpenAiAsync(settings.AiApiKey, settings.AiModel, fullSystemInstruction, customerMessage);
            }
            else
            {
                reply = await CallGeminiAsync(settings.AiApiKey, settings.AiModel, fullSystemInstruction, customerMessage);
            }

            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar mensagem via IA para {RemoteJid}. Usando fallback para menu padrão.", remoteJid);
            return null; // Fallback para o bot tradicional caso a IA falhe
        }
    }

    public async Task<TestAiResponse> TestConnectionAsync(string provider, string apiKey, string model, string? testMessage)
    {
        var response = new TestAiResponse();
        try
        {
            string message = string.IsNullOrWhiteSpace(testMessage)
                ? "Olá! Apresente-se brevemente como o atendente virtual inteligente da nossa empresa."
                : testMessage;

            string systemPrompt = "Você é o assistente virtual de teste da Kora AI. Responda em 1 frase curta confirmando que sua conexão com a inteligência artificial está funcionando perfeitamente.";

            string? reply;
            if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                reply = await CallOpenAiAsync(apiKey, model, systemPrompt, message);
            }
            else
            {
                reply = await CallGeminiAsync(apiKey, model, systemPrompt, message);
            }

            if (!string.IsNullOrWhiteSpace(reply))
            {
                response.Success = true;
                response.Reply = reply.Trim();
            }
            else
            {
                response.Success = false;
                response.Error = "A IA respondeu com conteúdo vazio.";
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = $"Erro de conexão: {ex.Message}";
        }

        return response;
    }

    private async Task<string?> CallGeminiAsync(string apiKey, string model, string systemInstruction, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Contains("gemini-1.5") || model.Contains("gemini-2.0"))
        {
            model = "gemini-flash-lite-latest";
        }

        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userMessage } }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 2048
            }
        };

        var requestJson = JsonSerializer.Serialize(payload);

        // Suporte a retry caso o Google Gemini retorne 503 (Unavailable / High Demand) ou 429 (Rate Limit)
        HttpResponseMessage? httpResponse = null;
        string responseBody = string.Empty;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            httpResponse = await _httpClient.PostAsync(endpoint, content);
            responseBody = await httpResponse.Content.ReadAsStringAsync();

            if (httpResponse.IsSuccessStatusCode)
            {
                break;
            }

            if ((int)httpResponse.StatusCode == 503 || (int)httpResponse.StatusCode == 429)
            {
                if (attempt < 3)
                {
                    await Task.Delay(1500 * attempt);
                    continue;
                }
            }

            throw new InvalidOperationException($"Google Gemini retornou erro ({httpResponse.StatusCode}): {responseBody}");
        }

        if (httpResponse == null || !httpResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Gemini retornou erro ({httpResponse?.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var contentObj) &&
                contentObj.TryGetProperty("parts", out var parts))
            {
                var fullText = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textProp))
                    {
                        fullText.Append(textProp.GetString());
                    }
                }

                if (fullText.Length > 0)
                {
                    return fullText.ToString().Trim();
                }
            }
        }

        return null;
    }

    private async Task<string?> CallOpenAiAsync(string apiKey, string model, string systemInstruction, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(model)) model = "gpt-4o-mini";

        string endpoint = "https://api.openai.com/v1/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 800
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var httpResponse = await _httpClient.SendAsync(request);
        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI retornou erro ({httpResponse.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() > 0)
        {
            var text = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return text;
        }

        return null;
    }

    public async Task<BotSettingsDto> GetSettingsAsync()
    {
        var tenantSettings = await _dbContext.TenantSettings.FirstOrDefaultAsync();
        if (tenantSettings != null)
        {
            return new BotSettingsDto
            {
                IsBotActiveGlobally = tenantSettings.IsBotActiveGlobally,
                RejoyCatalogUrl = tenantSettings.WebsiteUrl ?? string.Empty,
                IsAiEnabled = tenantSettings.IsAiEnabled,
                AiProvider = tenantSettings.AiProvider,
                AiApiKey = tenantSettings.AiApiKey ?? string.Empty,
                AiModel = tenantSettings.AiModel,
                AiSystemPrompt = !string.IsNullOrWhiteSpace(tenantSettings.AiSystemPrompt)
                    ? tenantSettings.AiSystemPrompt
                    : GetDefaultSystemPrompt(),
                IsWhatsAppCatalogLinkEnabled = tenantSettings.IsWhatsAppCatalogLinkEnabled,
                WhatsAppCatalogPhone = tenantSettings.WhatsAppCatalogPhone ?? string.Empty,
                ProductUrlPattern = tenantSettings.ProductUrlPattern ?? string.Empty,
                LastSyncAt = tenantSettings.LastCatalogSyncAt,
                TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
            };
        }

        var settingsDict = await _dbContext.Set<BotSetting>()
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        string GetVal(string key, string def) =>
            settingsDict.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : def;

        bool.TryParse(GetVal("IsAiEnabled", "false"), out bool isAiEnabled);
        bool.TryParse(GetVal("IsWhatsAppCatalogLinkEnabled", "true"), out bool isWhatsAppCatalogLinkEnabled);
        DateTime? lastSync = null;
        if (settingsDict.TryGetValue("LastCatalogSyncAt", out var syncStr) && DateTime.TryParse(syncStr, out var parsedDate))
        {
            lastSync = parsedDate;
        }

        return new BotSettingsDto
        {
            RejoyCatalogUrl = GetVal("RejoyCatalogUrl", string.Empty),
            IsAiEnabled = isAiEnabled,
            AiProvider = GetVal("AiProvider", "gemini"),
            AiApiKey = GetVal("AiApiKey", ""),
            AiModel = GetVal("AiModel", "gemini-flash-lite-latest"),
            AiSystemPrompt = GetVal("AiSystemPrompt", GetDefaultSystemPrompt()),
            IsWhatsAppCatalogLinkEnabled = isWhatsAppCatalogLinkEnabled,
            WhatsAppCatalogPhone = GetVal("WhatsAppCatalogPhone", ""),
            ProductUrlPattern = GetVal("ProductUrlPattern", string.Empty),
            LastSyncAt = lastSync,
            TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
        };
    }

    public async Task<string> GenerateCommentPublicReplyAsync(string username, string commentText)
    {
        string fallback = $"Olá, @{username}! Que bom ver seu comentário! Te enviamos uma mensagem privada no Direct com mais detalhes, confira lá!";
        try
        {
            var settings = await GetSettingsAsync();
            if (!settings.IsAiEnabled || string.IsNullOrWhiteSpace(settings.AiApiKey))
            {
                return fallback;
            }

            string systemPrompt = 
                "Você é a Kora, assistente virtual inteligente e prestativa de atendimento ao cliente.\n" +
                "Gere uma resposta pública bem curta (máximo 1 a 2 frases), simpática, acolhedora e prestativa para o comentário de um seguidor em um post do Instagram.\n" +
                "Comente brevemente com emojis adequados e convide-o cordialmente a conferir o Direct que acabamos de mandar com mais informações.\n" +
                "Responda EXCLUSIVAMENTE o texto final da resposta, sem aspas e sem tags adicionais.";

            string userMessage = $"Comentário de @{username}: \"{commentText}\"";

            string? reply = null;
            if (string.Equals(settings.AiProvider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                reply = await CallOpenAiAsync(settings.AiApiKey, settings.AiModel, systemPrompt, userMessage);
            }
            else
            {
                reply = await CallGeminiAsync(settings.AiApiKey, settings.AiModel, systemPrompt, userMessage);
            }

            if (!string.IsNullOrWhiteSpace(reply))
            {
                return reply.Trim().Trim('\"');
            }

            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gerar resposta de comentário com IA para @{Username}. Usando fallback.", username);
            return fallback;
        }
    }

    public static string GetDefaultSystemPrompt()
    {
        return "Você é a Kora, a assistente virtual inteligente e prestativa de atendimento ao cliente.\n" +
               "Seu objetivo é esclarecer dúvidas, apresentar produtos ou serviços disponíveis e prestar um atendimento ágil, educado e acolhedor.\n\n" +
               "Diretrizes de Atendimento:\n" +
               "• Seja sempre cordial, prestativa e objetiva nas respostas.\n" +
               "• Utilize as informações de produtos, serviços e histórico fornecidos para tirar dúvidas com precisão.\n" +
               "• Se o cliente solicitar falar com um atendente humano, informe que a equipe continuará o atendimento e inclua a tag [PAUSAR_BOT] no final da sua mensagem.";
    }
}
