using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuração de portas padrão Kestrel (porta 5001 para evitar qualquer conflito)
builder.WebHost.UseUrls("http://0.0.0.0:5001");

// 1. Configuração do EF Core com MySQL (Pomelo)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=127.0.0.1;Port=3307;Database=xerifebot_db;User=root;Password=rootpassword;";

var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, serverVersion, mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    });
});

// 2. Cache em memória para rastreamento de mensagens anti-falsos-positivos
builder.Services.AddMemoryCache();

// 3. HttpClient tipado para a Evolution API v2 e Instagram Graph API v21.0
builder.Services.AddHttpClient<IEvolutionApiService, EvolutionApiService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["EvolutionApi:BaseUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<IInstagramGraphService, InstagramGraphService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Instagram:BaseUrl"] ?? "https://graph.instagram.com/v21.0/";
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(25);
});

// 4. Injeção de dependência dos fluxos do Bot, Importação de Catálogo e Inteligência Artificial
builder.Services.AddScoped<IBotFlowService, BotFlowService>();
builder.Services.AddScoped<ICatalogImportService, CatalogImportService>();
builder.Services.AddHttpClient<IAiBotService, AiBotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IXerifeAiService>(sp => (IXerifeAiService)sp.GetRequiredService<IAiBotService>());

// 4.1 Worker opcional desacoplado para novos seguidores do Instagram
builder.Services.AddSingleton<NewFollowerWelcomeService>();
builder.Services.AddSingleton<INewFollowerWelcomeService>(sp => sp.GetRequiredService<NewFollowerWelcomeService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NewFollowerWelcomeService>());

// 5. Configuração do CORS ampla para o Angular (http://localhost:4200 e http://127.0.0.1:4200)
const string corsPolicyName = "AllowAngularDevClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 6. Controllers e Swagger com inclusão explícita de assembly
builder.Services.AddControllers()
    .AddApplicationPart(typeof(XerifeBot.Api.Controllers.AdminController).Assembly);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Xerife Board Games - Omnichannel Bot API (WhatsApp & Instagram)",
        Version = "v1",
        Description = "API de automação de atendimento via WhatsApp (Evolution API v2) e Instagram Graph API Oficial v21.0"
    });
});

var app = builder.Build();

// Inicialização e criação do schema do banco de dados (Code-First)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        logger.LogInformation("Verificando e inicializando schema do banco de dados MySQL...");
        db.Database.EnsureCreated();
        
        // Garantir que a tabela BotSettings exista caso o banco já tenha sido gerado anteriormente
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS `BotSettings` (
                `Key` VARCHAR(100) NOT NULL,
                `Value` LONGTEXT NOT NULL,
                `UpdatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Key`)
            ) CHARACTER SET utf8mb4;
        ");

        // Garantir que a tabela InstagramCommentLogs exista
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS `InstagramCommentLogs` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `CommentId` VARCHAR(100) NOT NULL,
                `MediaId` VARCHAR(100) NULL,
                `Username` VARCHAR(100) NOT NULL,
                `CommentText` LONGTEXT NOT NULL,
                `PublicReplyText` LONGTEXT NULL,
                `PrivateDmSent` TINYINT(1) NOT NULL DEFAULT 0,
                `CreatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                INDEX `IX_InstagramCommentLogs_CommentId` (`CommentId`),
                INDEX `IX_InstagramCommentLogs_CreatedAt` (`CreatedAt`)
            ) CHARACTER SET utf8mb4;
        ");

        // Garantir que a coluna Channel exista em CustomerSessions caso a tabela já existisse
        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `CustomerSessions`
                ADD COLUMN `Channel` INT NOT NULL DEFAULT 0;
            ");
        }
        catch
        {
            // Coluna já existe
        }
        
        logger.LogInformation("Banco de dados pronto para operação.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao inicializar banco de dados MySQL. Verifique se o container docker do MySQL está ativo.");
    }
}

// Swagger disponível sempre em dev/test
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Xerife Bot API v1");
    c.RoutePrefix = "swagger";
});

// Ordem estrita de middlewares ASP.NET Core 8
app.UseRouting();
app.UseCors(corsPolicyName);
app.UseAuthorization();

// Rota de checagem direta (Health Check)
app.MapGet("/api/health", () => Results.Ok(new { status = "online", timestamp = DateTime.UtcNow }));

// Mapeamento dos Controllers com aplicação obrigatória da política CORS
app.MapControllers().RequireCors(corsPolicyName);

// Log explícito de todos os endpoints mapeados ao iniciar
app.Lifetime.ApplicationStarted.Register(() =>
{
    var endpointSources = app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("================ ROTAS REGISTRADAS NA API ================");
    foreach (var source in endpointSources)
    {
        foreach (var endpoint in source.Endpoints)
        {
            logger.LogInformation(" -> Rota ativa: {Route}", endpoint.DisplayName);
        }
    }
    logger.LogInformation("==========================================================");
});

app.Run();
