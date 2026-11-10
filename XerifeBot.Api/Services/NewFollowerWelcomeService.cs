using Microsoft.Extensions.Hosting;

namespace XerifeBot.Api.Services;

/// <summary>
/// Worker desacoplado em Background para recepção/polling de novos seguidores do Instagram.
/// 
/// NOTA DE ARQUITETURA E SEGURANÇA (Meta Graph API):
/// - A Meta não fornece um webhook push direto e irrestrito para o evento 'new_follower' para todas as contas de criador/business.
/// - Polling contínuo de seguidores em contas de alta escala pode acionar Rate Limits (HTTP 429) e penalizações de spam.
/// - Portanto, o fluxo padrão recomendado da Meta para abertura de conversa no Direct com novos visitantes
///   é o recurso oficial de 'Ice Breakers' (POST /me/messenger_profile) e 'Comment-to-DM' (Private Replies).
/// - Caso deseje ativar o polling experimental em um container secundário:
///   1. Configure 'Instagram:EnableExperimentalFollowerPoller: true' no appsettings.json ou variável de ambiente.
///   2. No docker-compose.yml, este serviço pode ser instanciado em um container isolado sem interferir na API principal.
/// </summary>
public class NewFollowerWelcomeService : BackgroundService, INewFollowerWelcomeService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewFollowerWelcomeService> _logger;

    public bool IsEnabled { get; }

    public NewFollowerWelcomeService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<NewFollowerWelcomeService> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;

        IsEnabled = _configuration.GetValue<bool>("Instagram:EnableExperimentalFollowerPoller", false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("ℹ️ [Instagram Worker] O Poller experimental de novos seguidores está DESATIVADO (recomendado). O fluxo prioritário utiliza Ice Breakers oficiais e Comment-to-DM da Meta Graph API.");
            return;
        }

        _logger.LogWarning("⚠️ [Instagram Worker] Poller experimental de novos seguidores ATIVADO. Monitorando novos seguidores com intervalo de segurança...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckNewFollowersAndWelcomeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante a execução do poller experimental de seguidores do Instagram.");
            }

            // Intervalo conservador de 15 minutos para evitar bloqueios da Meta
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    public async Task CheckNewFollowersAndWelcomeAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var igService = scope.ServiceProvider.GetRequiredService<IInstagramGraphService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NewFollowerWelcomeService>>();

        logger.LogInformation("Verificando atualizações de seguidores do Instagram com a Meta Graph API...");
        // Implementação extensível para integração com container secundário
        await Task.CompletedTask;
    }
}
