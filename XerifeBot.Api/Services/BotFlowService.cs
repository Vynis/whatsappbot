using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

public class BotFlowService : IBotFlowService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BotFlowService> _logger;
    private static readonly CultureInfo PtBrCulture = new("pt-BR");

    public BotFlowService(AppDbContext dbContext, ILogger<BotFlowService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string?> ProcessMessageAsync(CustomerSession session, string incomingText)
    {
        if (string.IsNullOrWhiteSpace(incomingText))
            return null;

        string normalized = incomingText.Trim().ToLowerInvariant();
        string cleanName = !string.IsNullOrWhiteSpace(session.PushName) ? session.PushName.Split(' ')[0] : "amigo(a)";

        var settings = await _dbContext.TenantSettings.FirstOrDefaultAsync();
        string botName = !string.IsNullOrWhiteSpace(settings?.BotName) ? settings.BotName : "Xerife Board Games";
        string city = !string.IsNullOrWhiteSpace(settings?.CityOrRegion) ? settings.CityOrRegion : "sua região";
        string site = !string.IsNullOrWhiteSpace(settings?.WebsiteUrl) ? settings.WebsiteUrl : "https://xerifebg.rejoy.com.br/";

        // 1. Comando explícito de reativação ou saudações
        if (IsGreetingOrMenu(normalized))
        {
            return BuildMainMenu(cleanName, botName, city);
        }

        // 2. Opções do Menu
        switch (normalized)
        {
            case "1":
            case "opcao 1":
            case "opção 1":
            case "catalogo":
            case "catálogo":
                return await BuildCatalogResponseAsync(botName, site);

            case "2":
            case "opcao 2":
            case "opção 2":
            case "combos":
            case "promocao":
            case "promoção":
                return BuildPromotionsResponse(botName);

            case "3":
            case "opcao 3":
            case "opção 3":
            case "como funciona":
            case "aluguel":
            case "entregas":
                return BuildHowItWorksResponse(botName, city);

            case "4":
            case "opcao 4":
            case "opção 4":
            case "indicacao":
            case "indicação":
            case "indique":
                return await BuildGameRecommendationsAsync(botName);

            case "9":
            case "opcao 9":
            case "opção 9":
            case "atendente":
            case "humano":
            case "ajuda":
                // Pausar o bot por 12 horas e sinalizar atendimento humano
                session.IsBotPaused = true;
                session.BotPausedUntil = DateTime.UtcNow.AddHours(12);
                session.PauseReason = "Solicitou atendente (Opção 9)";
                await _dbContext.SaveChangesAsync();

                return BuildHumanHandoverResponse(cleanName, botName);

            default:
                // Fallback Silencioso: Não enviar spam de comando inválido
                _logger.LogInformation("Mensagem recebida '{IncomingText}' não corresponde ao menu. Fallback silencioso acionado.", incomingText);
                return null;
        }
    }

    private static bool IsGreetingOrMenu(string text)
    {
        string[] greetings = {
            "oi", "ola", "olá", "bom dia", "boa tarde", "boa noite",
            "menu", "#bot", "comecar", "começar", "iniciar", "alugar",
            "xerife", "board game", "jogos"
        };

        return greetings.Contains(text) || text.StartsWith("oi ") || text.StartsWith("olá ") || text.StartsWith("ola ");
    }

    private static string BuildMainMenu(string clientName, string botName, string city)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🤠 *Olá, {clientName}! Seja muito bem-vindo(a) à {botName}!* 🎲");
        sb.AppendLine($"Sua casa de jogos de tabuleiro em {city}.");
        sb.AppendLine();
        sb.AppendLine("Como posso te ajudar hoje? Digite o *número* da opção desejada:");
        sb.AppendLine();
        sb.AppendLine("1️⃣ 🎲 *Ver Catálogo e Jogos Disponíveis*");
        sb.AppendLine("2️⃣ 🔥 *Combos e Ofertas Promocionais*");
        sb.AppendLine("3️⃣ 📋 *Como funciona o aluguel/compra e entregas*");
        sb.AppendLine("4️⃣ 💡 *Me indique um jogo! (Sugestões por estilo)*");
        sb.AppendLine("9️⃣ 🤠 *Falar diretamente com Atendente Humano*");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("_Dica: A qualquer momento digite *menu* para voltar aqui._");

        return sb.ToString();
    }

    private async Task<string> BuildCatalogResponseAsync(string botName, string siteUrl)
    {
        var availableGames = await _dbContext.BoardGames
            .Where(g => g.IsAvailable)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.Name)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"🎲 *CATÁLOGO {botName.ToUpperInvariant()}* 🎲");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            sb.AppendLine("Confira todas as fotos, detalhes e regras no nosso site:");
            sb.AppendLine($"👉 {siteUrl}");
            sb.AppendLine();
        }
        sb.AppendLine("📦 *Jogos em Destaque Disponíveis no Acervo:*");
        sb.AppendLine();

        if (availableGames.Count == 0)
        {
            sb.AppendLine("_No momento nossos principais jogos estão alugados/indisponíveis, mas você pode consultar o site ou chamar um atendente na opção 9 para reservas!_");
        }
        else
        {
            foreach (var game in availableGames.Take(15))
            {
                string priceInfo = game.RentalPrice > 0 
                    ? $" | Locação: {game.RentalPrice.ToString("C", PtBrCulture)}"
                    : (game.SalePrice.HasValue ? $" | Venda: {game.SalePrice.Value.ToString("C", PtBrCulture)}" : "");
                sb.AppendLine($"• *{game.Name}*");
                sb.AppendLine($"  👥 {game.MinPlayers} a {game.MaxPlayers} jogadores{priceInfo}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Gostou de algum? Digite *9* para falar com o atendente e fechar seu pedido!");
        return sb.ToString();
    }

    private static string BuildPromotionsResponse(string botName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🔥 *COMBOS E PROMOÇÕES DE {botName.ToUpperInvariant()}* 🔥");
        sb.AppendLine("Junte a galera e aproveite para jogar mais por menos!");
        sb.AppendLine();
        sb.AppendLine("🥉 *Combo Leve 3, Pague 2*");
        sb.AppendLine("Escolha 3 jogos do acervo e o de menor valor sai *100% GRÁTIS*! 🎁");
        sb.AppendLine();
        sb.AppendLine("🥈 *Combo Fim de Semana Turbinado*");
        sb.AppendLine("Retire na sexta-feira e devolva na segunda-feira aproveitando o melhor valor!");
        sb.AppendLine();
        sb.AppendLine("Digite *1* para ver os jogos ou *9* para reservar seus combos com o atendente!");
        return sb.ToString();
    }

    private static string BuildHowItWorksResponse(string botName, string city)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📋 *COMO FUNCIONA O ATENDIMENTO EM {botName.ToUpperInvariant()}*");
        sb.AppendLine();
        sb.AppendLine("⏰ *Períodos de Locação e Retirada:*");
        sb.AppendLine("• *Fim de Semana Completo:* Você retira na sexta-feira e devolve apenas na segunda-feira! Muito mais tempo para jogar.");
        sb.AppendLine("• *Diárias:* Pacotes flexíveis para curtir nos dias de semana.");
        sb.AppendLine();
        sb.AppendLine("📝 *Cadastro Super Rápido:*");
        sb.AppendLine("• Envio simplificado de documento e comprovante diretamente pelo WhatsApp.");
        sb.AppendLine();
        sb.AppendLine($"🚗 *Entregas e Retiradas:*");
        sb.AppendLine($"• Atendemos {city}.");
        sb.AppendLine("• Consulte nossa equipe para entrega/coleta ou retirada em ponto de apoio.");
        sb.AppendLine();
        sb.AppendLine("Digite *1* para ver os jogos ou *9* para iniciar seu atendimento!");
        return sb.ToString();
    }

    private async Task<string> BuildGameRecommendationsAsync(string botName)
    {
        var games = await _dbContext.BoardGames.ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"💡 *INDICAÇÕES: O QUE JOGAR HOJE EM {botName.ToUpperInvariant()}?* 🎲");
        sb.AppendLine();
        sb.AppendLine("🎉 *Para Rir com Amigos e Família (Party Games):*");
        sb.AppendLine("• *Dixit:* Jogo visual, poético e super criativo (3 a 8 jogadores).");
        sb.AppendLine("• *Ito:* Cooperativo genial onde você precisa ordenar pistas secretas (2 a 8 jogadores).");
        sb.AppendLine("• *Spyfall:* Ache o espião na roda antes que ele descubra o local! (3 a 8 jogadores).");
        sb.AppendLine();
        sb.AppendLine("🏰 *Para Estratégia, Conquista e Negociação:*");
        sb.AppendLine("• *Catan:* O clássico mundial de colonização e troca de recursos (3 a 4 jogadores).");
        sb.AppendLine("• *Ticket to Ride:* Construa ferrovias pela Europa, fácil de aprender e viciante (2 a 5 jogadores).");
        sb.AppendLine();
        sb.AppendLine("Quantas pessoas vão jogar com você? Digite *9* e conte para a gente que indicamos o jogo perfeito para sua mesa!");
        return sb.ToString();
    }

    private static string BuildHumanHandoverResponse(string clientName, string botName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🤠 *Prontinho, {clientName}! Já passei a vez para nossa equipe de {botName}!*");
        sb.AppendLine();
        sb.AppendLine("Nosso atendente humano foi notificado e irá te responder por aqui em instantes.");
        sb.AppendLine();
        sb.AppendLine("🔇 _O assistente virtual foi pausado para que vocês conversem tranquilamente._");
        sb.AppendLine("_Caso queira reativar o bot no futuro, basta digitar *#bot* ou *menu*._");
        return sb.ToString();
    }
}
