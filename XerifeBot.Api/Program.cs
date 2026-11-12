using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using XerifeBot.Api.Data;
using XerifeBot.Api.Middlewares;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuração de portas padrão Kestrel (porta 5001)
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

// 3. Multi-Tenancy Context & Autenticação JWT
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? "XerifeBotOmnichannel_SaaS_Secret_Key_Secure_2026_Minimum_32_Chars!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "XerifeBotSaaS";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "XerifeBotSaaS";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
});

// 4. HttpClient tipado para a Evolution API v2 e Instagram Graph API v21.0
builder.Services.AddHttpClient<IEvolutionApiService, EvolutionApiService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["EvolutionApi:BaseUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// 5. Injeção de dependência dos fluxos do Bot, Catálogo e Inteligência Artificial
builder.Services.AddScoped<IBotFlowService, BotFlowService>();
builder.Services.AddScoped<ICatalogImportService, CatalogImportService>();
builder.Services.AddHttpClient<IAiBotService, AiBotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IXerifeAiService>(sp => (IXerifeAiService)sp.GetRequiredService<IAiBotService>());

// 6. CORS configurado para o Angular Dev e Produção
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

// 7. Controllers e Swagger com suporte a Bearer JWT Token
builder.Services.AddControllers()
    .AddApplicationPart(typeof(XerifeBot.Api.Controllers.AdminController).Assembly);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Board Game Bot SaaS - Omnichannel API (WhatsApp & Instagram)",
        Version = "v1",
        Description = "API SaaS Multi-tenant para locadoras e lojas de jogos de tabuleiro com IA generativa e atendimento integrado."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Insira o token JWT gerado no login: Bearer {seu_token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Inicialização, migração e seed do banco de dados (Code-First & Multi-Tenancy)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        logger.LogInformation("Verificando e inicializando schema multi-tenant no MySQL...");
        db.Database.EnsureCreated();

        // 1. Criar tabelas multi-tenant Tenants, TenantSettings e Users se não existirem
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS `Tenants` (
                `Id` CHAR(36) NOT NULL,
                `Name` VARCHAR(150) NOT NULL,
                `Slug` VARCHAR(80) NOT NULL,
                `BusinessType` INT NOT NULL DEFAULT 1,
                `WhatsAppInstanceName` VARCHAR(100) NULL,
                `InstagramAccountId` VARCHAR(100) NULL,
                `InstagramAccessToken` VARCHAR(500) NULL,
                `InstagramVerifyToken` VARCHAR(100) NULL,
                `Plan` VARCHAR(50) NOT NULL DEFAULT 'Free',
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `CreatedAt` DATETIME(6) NOT NULL,
                `UpdatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                UNIQUE INDEX `IX_Tenants_Slug` (`Slug`),
                INDEX `IX_Tenants_WhatsAppInstanceName` (`WhatsAppInstanceName`)
            ) CHARACTER SET utf8mb4;
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS `TenantSettings` (
                `TenantId` CHAR(36) NOT NULL,
                `BotName` VARCHAR(100) NOT NULL,
                `WelcomeMessage` VARCHAR(1000) NULL,
                `WebsiteUrl` VARCHAR(300) NULL,
                `CityOrRegion` VARCHAR(150) NULL,
                `IsBotActiveGlobally` TINYINT(1) NOT NULL DEFAULT 1,
                `IsAiEnabled` TINYINT(1) NOT NULL DEFAULT 0,
                `AiProvider` VARCHAR(50) NOT NULL DEFAULT 'gemini',
                `AiApiKey` VARCHAR(255) NULL,
                `AiModel` VARCHAR(100) NOT NULL DEFAULT 'gemini-flash-lite-latest',
                `AiSystemPrompt` LONGTEXT NULL,
                `LastCatalogSyncAt` DATETIME(6) NULL,
                `UpdatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`TenantId`)
            ) CHARACTER SET utf8mb4;
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS `Users` (
                `Id` CHAR(36) NOT NULL,
                `TenantId` CHAR(36) NOT NULL,
                `FullName` VARCHAR(150) NOT NULL,
                `Email` VARCHAR(180) NOT NULL,
                `PasswordHash` VARCHAR(255) NOT NULL,
                `Role` VARCHAR(50) NOT NULL DEFAULT 'Admin',
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `CreatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                UNIQUE INDEX `IX_Users_Email` (`Email`),
                INDEX `IX_Users_TenantId` (`TenantId`)
            ) CHARACTER SET utf8mb4;

            CREATE TABLE IF NOT EXISTS `InstagramCommentLogs` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `TenantId` CHAR(36) NOT NULL,
                `CommentId` VARCHAR(100) NOT NULL,
                `MediaId` VARCHAR(100) NULL,
                `Username` VARCHAR(100) NOT NULL,
                `CommentText` LONGTEXT NOT NULL,
                `PublicReplyText` LONGTEXT NULL,
                `PrivateDmSent` TINYINT(1) NOT NULL DEFAULT 0,
                `CreatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                INDEX `IX_InstagramCommentLogs_TenantId` (`TenantId`),
                INDEX `IX_InstagramCommentLogs_CommentId` (`CommentId`)
            ) CHARACTER SET utf8mb4;
        ");

        // 2. Garantir coluna TenantId nas entidades existentes
        string[] tablesWithTenant = { "CustomerSessions", "BoardGames", "MessageLogs", "InstagramCommentLogs" };
        foreach (var table in tablesWithTenant)
        {
            try
            {
                string sqlAddTenant = "ALTER TABLE `" + table + "` ADD COLUMN `TenantId` CHAR(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';";
                db.Database.ExecuteSqlRaw(sqlAddTenant);
            }
            catch { /* Coluna já existe */ }
        }

        // 2.1 Garantir coluna SalePrice em BoardGames e Channel em CustomerSessions
        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `BoardGames`
                ADD COLUMN `SalePrice` DECIMAL(10,2) NULL;
            ");
        }
        catch { /* Coluna já existe */ }

        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `CustomerSessions`
                ADD COLUMN `Channel` INT NOT NULL DEFAULT 0;
            ");
        }
        catch { /* Coluna já existe */ }

        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `CustomerSessions`
                ADD INDEX `IX_CustomerSessions_TenantId_Channel` (`TenantId`, `Channel`);
            ");
        }
        catch { /* Índice já existe */ }

        // 2.2 Atualizar índices em CustomerSessions para ser único por Tenant
        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `CustomerSessions`
                DROP INDEX `IX_CustomerSessions_RemoteJid`;
            ");
        }
        catch { /* Índice antigo já removido ou não existia */ }

        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE `CustomerSessions`
                ADD UNIQUE INDEX `IX_CustomerSessions_TenantId_RemoteJid` (`TenantId`, `RemoteJid`);
            ");
        }
        catch { /* Índice composto já existe */ }

        // 3. Seed: Garantir que a empresa pioneira 'Xerife Board Games' exista com dados preservados
        var xerifeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var xerifeTenant = db.Tenants.IgnoreQueryFilters().FirstOrDefault(t => t.Id == xerifeTenantId || t.Slug == "xerife");
        if (xerifeTenant == null)
        {
            xerifeTenant = new Tenant
            {
                Id = xerifeTenantId,
                Name = "Xerife Board Games",
                Slug = "xerife",
                BusinessType = BusinessType.Rental,
                WhatsAppInstanceName = builder.Configuration["EvolutionApi:InstanceName"] ?? "XerifeAtendimento",
                InstagramAccountId = builder.Configuration["Instagram:AccountId"],
                InstagramAccessToken = builder.Configuration["Instagram:AccessToken"],
                InstagramVerifyToken = builder.Configuration["Instagram:VerifyToken"],
                Plan = "Free",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Tenants.Add(xerifeTenant);
            db.SaveChanges();
            logger.LogInformation("Tenant pioneiro 'Xerife Board Games' registrado com sucesso.");
        }

        // 3.1 Garantir configurações da Xerife
        var xerifeSettings = db.TenantSettings.IgnoreQueryFilters().FirstOrDefault(s => s.TenantId == xerifeTenant.Id);
        if (xerifeSettings == null)
        {
            xerifeSettings = new TenantSettings
            {
                TenantId = xerifeTenant.Id,
                BotName = "Xerife Board Games",
                CityOrRegion = "Aparecida de Goiânia e região",
                WebsiteUrl = "https://xerifebg.rejoy.com.br/search",
                IsBotActiveGlobally = true,
                IsAiEnabled = false,
                AiProvider = "gemini",
                AiModel = "gemini-flash-lite-latest",
                WelcomeMessage = "🤠 Olá! Seja muito bem-vindo(a) à Xerife Board Games! 🎲\nSua locadora de jogos de tabuleiro em Aparecida de Goiânia e região.",
                UpdatedAt = DateTime.UtcNow
            };
            db.TenantSettings.Add(xerifeSettings);
            db.SaveChanges();
        }

        // 3.1.1 Garantir BotSettings legado sempre sincronizado
        try
        {
            var legacyActive = db.BotSettings.Find("IsBotActiveGlobally");
            if (legacyActive == null)
            {
                db.BotSettings.Add(new BotSetting { Key = "IsBotActiveGlobally", Value = "true", UpdatedAt = DateTime.UtcNow });
                db.SaveChanges();
            }
            else if (legacyActive.Value != "true")
            {
                legacyActive.Value = "true";
                legacyActive.UpdatedAt = DateTime.UtcNow;
                db.SaveChanges();
            }
        }
        catch { }

        // 3.2 Garantir usuário Administrador da Xerife
        var xerifeAdmin = db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Email == "admin@xerife.com.br");
        if (xerifeAdmin == null)
        {
            xerifeAdmin = new User
            {
                Id = Guid.NewGuid(),
                TenantId = xerifeTenant.Id,
                FullName = "Administrador Xerife",
                Email = "admin@xerife.com.br",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminXerife2026!"),
                Role = UserRoles.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(xerifeAdmin);
            db.SaveChanges();
            logger.LogInformation("Usuário Administrador padrão criado: admin@xerife.com.br / AdminXerife2026!");
        }

        // 3.3 Migrar dados órfãos/antigos atribuindo ao Tenant Xerife
        string xerifeIdStr = xerifeTenant.Id.ToString();
        db.Database.ExecuteSqlInterpolated($"UPDATE `CustomerSessions` SET `TenantId` = {xerifeIdStr} WHERE `TenantId` = '00000000-0000-0000-0000-000000000000' OR `TenantId` IS NULL;");
        db.Database.ExecuteSqlInterpolated($"UPDATE `BoardGames` SET `TenantId` = {xerifeIdStr} WHERE `TenantId` = '00000000-0000-0000-0000-000000000000' OR `TenantId` IS NULL;");
        db.Database.ExecuteSqlInterpolated($"UPDATE `MessageLogs` SET `TenantId` = {xerifeIdStr} WHERE `TenantId` = '00000000-0000-0000-0000-000000000000' OR `TenantId` IS NULL;");
        db.Database.ExecuteSqlInterpolated($"UPDATE `InstagramCommentLogs` SET `TenantId` = {xerifeIdStr} WHERE `TenantId` = '00000000-0000-0000-0000-000000000000' OR `TenantId` IS NULL;");

        logger.LogInformation("Banco de dados pronto para operação Multi-Tenant.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao inicializar banco de dados MySQL Multi-Tenant.");
    }
}

// Swagger disponível sempre em dev/test
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Board Game Bot SaaS API v1");
    c.RoutePrefix = "swagger";
});

// Ordem estrita de middlewares ASP.NET Core 8 com Autenticação e Multi-Tenancy
app.UseRouting();
app.UseCors(corsPolicyName);
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// Rota de checagem direta (Health Check)
app.MapGet("/api/health", () => Results.Ok(new { status = "online", mode = "saas_multi_tenant", timestamp = DateTime.UtcNow }));

// Mapeamento dos Controllers
app.MapControllers().RequireCors(corsPolicyName);

app.Run();
