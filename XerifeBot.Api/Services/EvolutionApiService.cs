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
    private readonly string _defaultWebhookUrl;

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
        _defaultWebhookUrl = configuration["EvolutionApi:WebhookUrl"] ?? "http://xerife_api:5001/api/whatsapp/webhook";

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

    public async Task<bool> CreateInstanceAsync(string instanceName, string? webhookUrl = null)
    {
        try
        {
            var existing = await GetInstanceInfoAsync(instanceName);
            if (existing != null && existing.Status != "deleted" && existing.Status != "not_found")
            {
                _logger.LogInformation("Instância '{InstanceName}' já existe na Evolution API.", instanceName);
                if (!string.IsNullOrWhiteSpace(webhookUrl ?? _defaultWebhookUrl))
                {
                    await SetWebhookAsync(instanceName, webhookUrl ?? _defaultWebhookUrl);
                }
                return true;
            }

            string endpoint = $"{_baseUrl.TrimEnd('/')}/instance/create";
            var payload = new
            {
                instanceName = instanceName,
                token = _apiKey,
                qrcode = true,
                integration = "WHATSAPP-BAILEYS",
                reject_call = false,
                groups_ignore = true,
                always_online = false,
                read_messages = false,
                read_status = false,
                sync_full_history = false
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Evolution API retornou status {Status} ao criar instância {InstanceName}: {Body}",
                    response.StatusCode, instanceName, responseBody);
                return false;
            }

            _logger.LogInformation("Instância '{InstanceName}' criada com sucesso na Evolution API.", instanceName);

            string targetWebhook = webhookUrl ?? _defaultWebhookUrl;
            if (!string.IsNullOrWhiteSpace(targetWebhook))
            {
                await SetWebhookAsync(instanceName, targetWebhook);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar instância '{InstanceName}' na Evolution API.", instanceName);
            return false;
        }
    }

    public async Task<bool> SetWebhookAsync(string instanceName, string webhookUrl)
    {
        try
        {
            string endpoint = $"{_baseUrl.TrimEnd('/')}/webhook/set/{instanceName}";
            var payload = new
            {
                webhook = new
                {
                    enabled = true,
                    url = webhookUrl,
                    byEvents = false,
                    base64 = false,
                    events = new[]
                    {
                        "MESSAGES_UPSERT",
                        "CONNECTION_UPDATE"
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook configurado com sucesso para {InstanceName} -> {Url}", instanceName, webhookUrl);
                return true;
            }

            string err = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Falha ao configurar webhook para {InstanceName}: {Error}", instanceName, err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao configurar webhook para {InstanceName}", instanceName);
            return false;
        }
    }

    public async Task<WhatsAppConnectResultDto> ConnectInstanceAsync(string instanceName)
    {
        var result = new WhatsAppConnectResultDto
        {
            InstanceName = instanceName
        };

        try
        {
            var info = await GetInstanceInfoAsync(instanceName);
            if (info != null && string.Equals(info.Status, "open", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "open";
                result.IsConnected = true;
                result.Message = "O WhatsApp desta empresa já está conectado e pronto para uso!";
                return result;
            }

            if (info == null || info.Status == "close")
            {
                await CreateInstanceAsync(instanceName);
            }

            string endpoint = $"{_baseUrl.TrimEnd('/')}/instance/connect/{instanceName}";
            var response = await _httpClient.GetAsync(endpoint);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                result.Status = "error";
                result.Message = $"Erro ao obter QR Code da Evolution API: {response.StatusCode}";
                return result;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("base64", out var base64El) && !string.IsNullOrWhiteSpace(base64El.GetString()))
            {
                result.QrCodeBase64 = base64El.GetString();
                result.Status = "connecting";
                result.Message = "QR Code gerado com sucesso! Aponte a câmera do WhatsApp para conectar.";
            }
            else if (root.TryGetProperty("qrcode", out var qrObj) && qrObj.TryGetProperty("base64", out var qrBase64))
            {
                result.QrCodeBase64 = qrBase64.GetString();
                result.Status = "connecting";
                result.Message = "QR Code gerado com sucesso! Aponte a câmera do WhatsApp para conectar.";
            }

            if (root.TryGetProperty("pairingCode", out var pairEl))
            {
                result.PairingCode = pairEl.GetString();
            }

            if (root.TryGetProperty("instance", out var instEl) && instEl.TryGetProperty("status", out var stEl))
            {
                string status = stEl.GetString() ?? "close";
                result.Status = status;
                result.IsConnected = string.Equals(status, "open", StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao conectar instância WhatsApp '{InstanceName}'.", instanceName);
            result.Status = "error";
            result.Message = $"Exceção ao conectar: {ex.Message}";
            return result;
        }
    }

    public async Task<bool> LogoutInstanceAsync(string instanceName)
    {
        try
        {
            string endpoint = $"{_baseUrl.TrimEnd('/')}/instance/logout/{instanceName}";
            var response = await _httpClient.DeleteAsync(endpoint);
            _logger.LogInformation("Desconectando instância '{InstanceName}' (Status: {StatusCode})", instanceName, response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desconectar instância '{InstanceName}'", instanceName);
            return false;
        }
    }

    public async Task<bool> RestartInstanceAsync(string instanceName)
    {
        try
        {
            string endpoint = $"{_baseUrl.TrimEnd('/')}/instance/restart/{instanceName}";
            var response = await _httpClient.PutAsync(endpoint, null);
            _logger.LogInformation("Reiniciando instância '{InstanceName}' (Status: {StatusCode})", instanceName, response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao reiniciar instância '{InstanceName}'", instanceName);
            return false;
        }
    }

    public async Task<bool> DeleteInstanceAsync(string instanceName)
    {
        try
        {
            string endpoint = $"{_baseUrl.TrimEnd('/')}/instance/delete/{instanceName}";
            var response = await _httpClient.DeleteAsync(endpoint);
            _logger.LogInformation("Removendo instância '{InstanceName}' da Evolution API (Status: {StatusCode})", instanceName, response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover instância '{InstanceName}'", instanceName);
            return false;
        }
    }
}
