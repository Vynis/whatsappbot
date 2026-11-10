namespace XerifeBot.Api.Services;

/// <summary>
/// Serviço opcional/desacoplado para monitoramento experimental de novos seguidores do Instagram.
/// Por padrão desativado no appsettings.json (EnableExperimentalFollowerPoller: false)
/// para evitar bloqueios ou consumo agressivo da cota da Meta Graph API.
/// </summary>
public interface INewFollowerWelcomeService
{
    bool IsEnabled { get; }
    Task CheckNewFollowersAndWelcomeAsync(CancellationToken cancellationToken = default);
}
