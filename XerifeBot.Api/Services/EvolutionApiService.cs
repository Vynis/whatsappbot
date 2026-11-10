using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using XerifeBot.Api.Dtos;

namespace XerifeBot.Api.Services;

public class EvolutionApiService : IEvolutionApiService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<EvolutionApiService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public EvolutionApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<EvolutionApiService> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;

        _baseUrl = configuration["EvolutionApi:BaseUrl"] ?? "http://localhost:8080";
        _apiKey = configuration["EvolutionApi:ApiKey"] ?? "XerifeBG_Token_Secreto_2026";

        if (!_httpClient.DefaultRequestHeaders.Contains("apikey"))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
        }
    }

    public async Task<bool> SendTextAsync(string instance, string remoteJid, string text)
    {
        try
        {
            // Delay humanizado anti-ban aleatório entre 2000ms e 4000ms
            int delayMs = RandomNumberGenerator.GetInt32(2000, 4001);

            // Marca na memória que o bot está enviando mensagem para este JID nos próximos 15 segundos
            // Isso previne que o evento fromMe=true seja interpretado erroneamente como atendimento humano
            string jidWindowKey = $"bot_sent_window_{remoteJid}";
            _memoryCache.Set(jidWindowKey, true, TimeSpan.FromSeconds(15));

            var payload = new EvolutionSendTextRequest
            {
                Number = remoteJid,
                Text = text,
                Delay = delayMs,
                LinkPreview = true
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            string endpoint = $"{_baseUrl.TrimEnd('/')}/message/sendText/{instance}";
            _logger.LogInformation("Enviando mensagem via Evolution API para {RemoteJid} com delay humanizado de {DelayMs}ms", remoteJid, delayMs);

            var response = await _httpClient.PostAsync(endpoint, jsonContent);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha ao enviar mensagem via Evolution API ({StatusCode}): {ResponseBody}", response.StatusCode, responseBody);
                return false;
            }

            // Registrar também o ID da mensagem retornado pela Evolution API
            try
            {
                var sendResult = JsonSerializer.Deserialize<EvolutionSendResponse>(responseBody);
                if (sendResult?.Key?.Id != null)
                {
                    string msgIdKey = $"bot_msg_id_{sendResult.Key.Id}";
                    _memoryCache.Set(msgIdKey, true, TimeSpan.FromMinutes(2));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível extrair ID da mensagem retornada pela Evolution API.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao disparar SendTextAsync para {RemoteJid}", remoteJid);
            return false;
        }
    }

    public async Task<bool> SendMediaAsync(string instance, string remoteJid, string mediaUrl, string caption)
    {
        try
        {
            string jidWindowKey = $"bot_sent_window_{remoteJid}";
            _memoryCache.Set(jidWindowKey, true, TimeSpan.FromSeconds(15));

            var payload = new EvolutionSendMediaRequest
            {
                Number = remoteJid,
                MediaType = "image",
                Media = mediaUrl,
                Caption = caption,
                Delay = 2000
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            string endpoint = $"{_baseUrl.TrimEnd('/')}/message/sendMedia/{instance}";
            var response = await _httpClient.PostAsync(endpoint, jsonContent);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao disparar SendMediaAsync para {RemoteJid}", remoteJid);
            return false;
        }
    }

    public bool IsMessageFromBot(string remoteJid, string? messageId)
    {
        // 1. Checa se o ID da mensagem está registrado como enviado pelo Bot
        if (!string.IsNullOrWhiteSpace(messageId) && _memoryCache.TryGetValue($"bot_msg_id_{messageId}", out _))
        {
            return true;
        }

        // 2. Checa se o JID tem envio recente ativo registrado pelo Bot
        if (!string.IsNullOrWhiteSpace(remoteJid) && _memoryCache.TryGetValue($"bot_sent_window_{remoteJid}", out _))
        {
            return true;
        }

        return false;
    }

    public async Task<InstanceInfoDto?> GetInstanceInfoAsync(string instanceName)
    {
        try
        {
            string url = $"{_baseUrl.TrimEnd('/')}/instance/fetchInstances";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string? name = null;
                if (el.TryGetProperty("name", out var n)) name = n.GetString();
                if (string.IsNullOrEmpty(name) && el.TryGetProperty("instanceName", out var iname)) name = iname.GetString();
                if (string.IsNullOrEmpty(name) && el.TryGetProperty("instance", out var iobj) && iobj.TryGetProperty("instanceName", out var subName)) name = subName.GetString();

                if (string.Equals(name, instanceName, StringComparison.OrdinalIgnoreCase))
                {
                    string status = "close";
                    if (el.TryGetProperty("connectionStatus", out var cs)) status = cs.GetString() ?? "close";
                    else if (el.TryGetProperty("status", out var st)) status = st.GetString() ?? "close";

                    string? owner = null;
                    if (el.TryGetProperty("owner", out var ow)) owner = ow.GetString();
                    else if (el.TryGetProperty("ownerJid", out var oj)) owner = oj.GetString();

                    string? profileName = null;
                    if (el.TryGetProperty("profileName", out var pn)) profileName = pn.GetString();

                    return new InstanceInfoDto
                    {
                        InstanceName = name ?? instanceName,
                        Status = status,
                        OwnerJid = owner,
                        ProfileName = profileName
                    };
                }
            }

            return new InstanceInfoDto
            {
                InstanceName = instanceName,
                Status = "close"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar informacoes da instancia {InstanceName}", instanceName);
            return null;
        }
    }
}
