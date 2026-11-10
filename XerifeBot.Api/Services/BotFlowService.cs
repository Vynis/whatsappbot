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

        // 1. Comando explícito de reativação ou saudações
        if (IsGreetingOrMenu(normalized))
        {
            return BuildMainMenu(cleanName);
        }

        // 2. Opções do Menu
        switch (normalized)
        {
            case "1":
            case "opcao 1":
            case "opção 1":
            case "catalogo":
            case "catálogo":
                return await BuildCatalogResponseAsync();

            case "2":
            case "opcao 2":
            case "opção 2":
            case "combos":
            case "promocao":
            case "promoção":
                return BuildPromotionsResponse();

            case "3":
            case "opcao 3":
            case "opção 3":
            case "como funciona":
            case "aluguel":
            case "entregas":
                return BuildHowItWorksResponse();

            case "4":
            case "opcao 4":
            case "opção 4":
            case "indicacao":
            case "indicação":
            case "indique":
                return await BuildGameRecommendationsAsync();

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

                return BuildHumanHandoverResponse(cleanName);

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

    private static string BuildMainMenu(string clientName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🤠 *Olá, {clientName}! Seja muito bem-vindo(a) à Xerife Board Games!* 🎲");
        sb.AppendLine("Sua locadora de jogos de tabuleiro em Aparecida de Goiânia e região.");
        sb.AppendLine();
        sb.AppendLine("Como posso te ajudar hoje? Digite o *número* da opção desejada:");
        sb.AppendLine();
        sb.AppendLine("1️⃣ 🎲 *Ver Catálogo Online e Jogos Disponíveis*");
        sb.AppendLine("2️⃣ 🔥 *Combos Promocionais (Leve Mais, Pague Menos!)*");
        sb.AppendLine("3️⃣ 📋 *Como funciona o aluguel, cadastro e entregas*");
        sb.AppendLine("4️⃣ 💡 *Me indique um jogo! (Sugestões por estilo)*");
        sb.AppendLine("9️⃣ 🤠 *Falar diretamente com o Xerife (Atendente Humano)*");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("_Dica: A qualquer momento digite *menu* para voltar aqui._");

        return sb.ToString();
    }

    private async Task<string> BuildCatalogResponseAsync()
    {
        var availableGames = await _dbContext.BoardGames
            .Where(g => g.IsAvailable)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.Name)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("🎲 *CATÁLOGO XERIFE BOARD GAMES* 🎲");
        sb.AppendLine();
        sb.AppendLine("Confira todas as fotos, detalhes e regras no nosso site oficial:");
        sb.AppendLine("👉 https://xerifebg.rejoy.com.br/");
        sb.AppendLine();
        sb.AppendLine("📦 *Jogos em Destaque Disponíveis para Retirada Imediata:*");
        sb.AppendLine();

        if (availableGames.Count == 0)
        {
            sb.AppendLine("_No momento nossos principais jogos estão alugados, mas você pode consultar o site ou chamar o Xerife na opção 9 para reservas!_");
        }
        else
        {
            foreach (var game in availableGames)
            {
                sb.AppendLine($"• *{game.Name}*");
                sb.AppendLine($"  👥 {game.MinPlayers} a {game.MaxPlayers} jogadores | 🌐 Consulte valores e períodos no site");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Gostou de algum? Digite *9* para falar com o Xerife e fechar seu aluguel, ou digite *2* para conhecer nossos combos!");
        return sb.ToString();
    }

    private static string BuildPromotionsResponse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("🔥 *COMBOS PROMOCIONAIS DA XERIFE* 🔥");
        sb.AppendLine("Junte a galera e aproveite o fim de semana jogando mais por menos!");
        sb.AppendLine();
        sb.AppendLine("🥉 *Combo Leve 3, Pague 2*");
        sb.AppendLine("Escolha 3 jogos do catálogo e o de menor valor de locação sai *100% GRÁTIS*! 🎁");
        sb.AppendLine();
        sb.AppendLine("🥈 *Combo Leve 4, Pague 3*");
        sb.AppendLine("Alugue 4 jogos de tabuleiro e pague apenas os 3 de maior valor.");
        sb.AppendLine();
        sb.AppendLine("💡 *Regras do combo:*");
        sb.AppendLine("- Válido para locações de final de semana (sexta a segunda).");
        sb.AppendLine("- Perfeito para variar entre jogos rápidos (party games) e jogos de estratégia!");
        sb.AppendLine();
        sb.AppendLine("Digite *1* para ver os jogos ou *9* para reservar seus combos com o Xerife!");
        return sb.ToString();
    }

    private static string BuildHowItWorksResponse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("📋 *COMO FUNCIONA O ALUGUEL NA XERIFE BOARD GAMES*");
        sb.AppendLine();
        sb.AppendLine("⏰ *Período Padrão de Locação:*");
        sb.AppendLine("• *Fim de Semana Completo:* Você retira na sexta-feira e devolve apenas na segunda-feira! Muito mais tempo para jogar.");
        sb.AppendLine("• *Diárias:* Também temos pacotes para dias de semana sob consulta.");
        sb.AppendLine();
        sb.AppendLine("📝 *Cadastro Super Rápido:*");
        sb.AppendLine("• Basta enviar foto do seu documento (RG/CNH) e comprovante de endereço atualizado.");
        sb.AppendLine("• Assinatura digital do termo de locação simples pelo WhatsApp.");
        sb.AppendLine();
        sb.AppendLine("🚗 *Entregas e Retiradas:*");
        sb.AppendLine("• Atendemos Aparecida de Goiânia e Goiânia.");
        sb.AppendLine("• Fazemos entrega e coleta via motoboy parceiro (consulte a taxa para seu bairro).");
        sb.AppendLine("• Também é possível combinar a retirada sem custo em nosso ponto de apoio!");
        sb.AppendLine();
        sb.AppendLine("Digite *1* para ver os jogos ou *9* para iniciar seu cadastro com o atendente!");
        return sb.ToString();
    }

    private async Task<string> BuildGameRecommendationsAsync()
    {
        var games = await _dbContext.BoardGames.ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("💡 *INDICAÇÕES DO XERIFE: O QUE JOGAR HOJE?* 🎲");
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
        sb.AppendLine("🧟 *Cooperativo / Aventura Intensa:*");
        sb.AppendLine("• *Zombicide:* Todos os jogadores se unem para sobreviver ao apocalipse zumbi! (1 a 6 jogadores).");
        sb.AppendLine();
        sb.AppendLine("Quantas pessoas vão jogar com você? Digite *9* e conte para o Xerife que ele escolhe o jogo perfeito para sua noite!");
        return sb.ToString();
    }

    private static string BuildHumanHandoverResponse(string clientName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🤠 *Prontinho, {clientName}! Já passei a vez para o Xerife!*");
        sb.AppendLine();
        sb.AppendLine("Nosso atendente humano foi notificado e irá te responder por aqui em instantes.");
        sb.AppendLine();
        sb.AppendLine("🔇 _O assistente virtual foi pausado para que vocês conversem tranquilamente._");
        sb.AppendLine("_Caso queira reativar o bot no futuro, basta digitar *#bot* ou *menu*._");
        return sb.ToString();
    }
}
