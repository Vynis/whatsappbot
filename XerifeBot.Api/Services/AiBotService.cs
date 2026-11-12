using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

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

            var gamesContext = new StringBuilder();
            gamesContext.AppendLine("\n--- JOGOS DISPONÍVEIS AGORA NO ACERVO DA LOJA (CONFIRMADOS EM ESTOQUE) ---");
            foreach (var g in availableGames)
            {
                if (!string.IsNullOrWhiteSpace(g.LinkLudopedia))
                {
                    gamesContext.AppendLine($"• {g.Name} - Ludopedia: {g.LinkLudopedia}");
                }
                else
                {
                    gamesContext.AppendLine($"• {g.Name}");
                }
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
                    string sender = log.Sender == MessageSender.Customer ? "Cliente" : "Xerife (Você)";
                    historyContext.AppendLine($"{sender}: {log.Content}");
                }
            }

            // 3. Montar o Prompt do Sistema completo
            string systemPrompt = !string.IsNullOrWhiteSpace(settings.AiSystemPrompt)
                ? settings.AiSystemPrompt
                : GetDefaultSystemPrompt();

            string channelFormatting = channel == CommunicationChannel.Instagram
                ? "• Responda para o Instagram Direct: tom direto e envolvente, parágrafos curtos, emojis descontraídos (🤠🎲).\n"
                : "• Use formatação do WhatsApp (*negrito*, quebras de linha limpas).\n";

            string fullSystemInstruction = $"{systemPrompt}\n{gamesContext}\n{historyContext}\n" +
                "REGRAS OBRIGATÓRIAS SOBRE PREÇOS E LOCAÇÃO:\n" +
                "• NUNCA invente, estime ou informe valores em R$ ou valores de 'diária' para os jogos. A lista de acervo NÃO possui valores de aluguel.\n" +
                "• Os valores exatos de aluguel e os períodos (ex: 5 dias, 7 dias) ficam EXCLUSIVAMENTE no site da loja: https://xerifebg.rejoy.com.br/\n" +
                "• Quando o cliente perguntar sobre preços ou valores de qualquer jogo, informe com entusiasmo que o jogo está disponível e oriente-o a conferir os valores e pacotes de dias diretamente no site https://xerifebg.rejoy.com.br/ ou se ofereça para chamar o atendente humano.\n" +
                "• Responda em Português do Brasil com tom amigável e descontraído de um xerife/atendente de jogos de tabuleiro.\n" +
                channelFormatting +
                "• Se o cliente pedir para falar com atendente ou quiser fechar o aluguel, encerre avisando com entusiasmo que um atendente humano assumirá em instantes E inclua a tag [PAUSAR_BOT] no final do texto para que o sistema acione a pausa automática.";

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
                ? "Olá! Apresente-se brevemente como o atendente virtual da Xerife Board Games."
                : testMessage;

            string systemPrompt = "Você é o atendente de teste da Xerife Board Games. Responda em 1 frase curta confirmando que sua conexão está funcionando perfeitamente.";

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
                RejoyCatalogUrl = tenantSettings.WebsiteUrl ?? "https://xerifebg.rejoy.com.br/search",
                IsAiEnabled = tenantSettings.IsAiEnabled,
                AiProvider = tenantSettings.AiProvider,
                AiApiKey = tenantSettings.AiApiKey ?? string.Empty,
                AiModel = tenantSettings.AiModel,
                AiSystemPrompt = !string.IsNullOrWhiteSpace(tenantSettings.AiSystemPrompt)
                    ? tenantSettings.AiSystemPrompt
                    : GetDefaultSystemPrompt(),
                LastSyncAt = tenantSettings.LastCatalogSyncAt,
                TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
            };
        }

        var settingsDict = await _dbContext.Set<BotSetting>()
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        string GetVal(string key, string def) =>
            settingsDict.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : def;

        bool.TryParse(GetVal("IsAiEnabled", "false"), out bool isAiEnabled);
        DateTime? lastSync = null;
        if (settingsDict.TryGetValue("LastCatalogSyncAt", out var syncStr) && DateTime.TryParse(syncStr, out var parsedDate))
        {
            lastSync = parsedDate;
        }

        return new BotSettingsDto
        {
            RejoyCatalogUrl = GetVal("RejoyCatalogUrl", "https://xerifebg.rejoy.com.br/search"),
            IsAiEnabled = isAiEnabled,
            AiProvider = GetVal("AiProvider", "gemini"),
            AiApiKey = GetVal("AiApiKey", ""),
            AiModel = GetVal("AiModel", "gemini-flash-lite-latest"),
            AiSystemPrompt = GetVal("AiSystemPrompt", GetDefaultSystemPrompt()),
            LastSyncAt = lastSync,
            TotalGamesInDb = await _dbContext.BoardGames.CountAsync()
        };
    }

    public async Task<string> GenerateCommentPublicReplyAsync(string username, string commentText)
    {
        string fallback = $"Opa, @{username}! 🤠🎲 Que demais ver seu comentário! Te mandamos uma mensagem especial no Direct com sugestões e um presente, dá uma olhada lá!";
        try
        {
            var settings = await GetSettingsAsync();
            if (!settings.IsAiEnabled || string.IsNullOrWhiteSpace(settings.AiApiKey))
            {
                return fallback;
            }

            string systemPrompt = 
                "Você é o Xerife, o atendente virtual entusiasmado da Xerife Board Games (@xerifebg).\n" +
                "Gere uma resposta pública bem curta (máximo 1 a 2 frases), descontraída, simpática e empolgada para o comentário de um seguidor em um post do Instagram.\n" +
                "Use emojis de jogos (🤠🎲), comente brevemente e convide-o amigavelmente a conferir o Direct que acabamos de mandar com dicas e cupom de 1ª locação.\n" +
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
        return "Você é o Xerife, o atendente virtual inteligente da loja Xerife Board Games (Aparecida de Goiânia/GO). " +
               "Seu objetivo é ajudar os clientes a alugar jogos de tabuleiro de forma rápida, divertida e prática.\n\n" +
               "Regras da Loja:\n" +
               "• Locação de jogos em Aparecida de Goiânia e região metropolitana de Goiânia.\n" +
               "• A loja NÃO possui serviço de entregador próprio, mas o cliente pode retirar pessoalmente em Aparecida de Goiânia ou mandar Uber Flash / 99 Entrega.\n" +
               "• Promoções ativas: Alugue 3 pague 2 (Combo 3x2) e Alugue 4 pague 3 (Combo 4x3).\n" +
               "• Somente recomende jogos que estejam na lista de jogos disponíveis informada abaixo.\n" +
               "• IMPORTANTE SOBRE PREÇOS: Você NÃO sabe os valores em R$ de aluguel nem valores de diária. Os valores e pacotes de dias (ex: 5 dias, 7 dias) devem ser consultados pelo cliente exclusivamente no site: https://xerifebg.rejoy.com.br/\n" +
               "• Se o cliente disser que quer fechar o aluguel, pagar, passar o endereço, ou pedir para falar com uma pessoa, avise com entusiasmo que o atendente humano assumirá o atendimento imediatamente E inclua a tag [PAUSAR_BOT] no final do texto.";
    }
}
