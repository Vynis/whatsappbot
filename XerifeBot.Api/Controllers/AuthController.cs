using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableCors("AllowAngularDevClient")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Verificar se o e-mail já está em uso no sistema (ignora query filters)
        bool emailExists = await _dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == normalizedEmail);

        if (emailExists)
        {
            return BadRequest(new { message = "Este endereço de e-mail já está cadastrado na plataforma." });
        }

        // 2. Gerar Slug limpo e único para a empresa/tenant
        string baseSlug = GenerateSlug(request.CompanyName);
        string slug = baseSlug;
        int counter = 1;
        while (await _dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        // 3. Criar a Empresa (Tenant)
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName.Trim(),
            Slug = slug,
            BusinessType = request.BusinessType,
            WhatsAppInstanceName = $"evo_{slug}_{Guid.NewGuid().ToString("N")[..6]}",
            Plan = "Free",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // 4. Criar as Configurações Iniciais da Empresa
        var settings = new TenantSettings
        {
            TenantId = tenant.Id,
            BotName = request.CompanyName.Trim(),
            CityOrRegion = request.CityOrRegion?.Trim(),
            WebsiteUrl = request.WebsiteUrl?.Trim(),
            IsBotActiveGlobally = true,
            IsAiEnabled = false,
            AiProvider = "gemini",
            AiModel = "gemini-flash-lite-latest",
            WelcomeMessage = $"Olá! Seja muito bem-vindo(a) à *{request.CompanyName.Trim()}*! 🎲\nComo podemos te ajudar hoje?",
            UpdatedAt = DateTime.UtcNow
        };

        // 5. Criar o Usuário Administrador com senha criptografada via BCrypt
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            Role = UserRoles.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Tenants.Add(tenant);
        _dbContext.TenantSettings.Add(settings);
        _dbContext.Users.Add(user);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Novo Tenant '{TenantName}' ({TenantSlug}) e usuário '{Email}' cadastrados com sucesso!",
            tenant.Name, tenant.Slug, user.Email);

        // 6. Gerar Token JWT de acesso
        string token = _jwtTokenService.GenerateToken(user, tenant);

        return Ok(new AuthResponseDto
        {
            Token = token,
            User = new UserSummaryDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                TenantId = user.TenantId
            },
            Tenant = new TenantSummaryDto
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Slug = tenant.Slug,
                BusinessType = tenant.BusinessType,
                Plan = tenant.Plan,
                WhatsAppInstanceName = tenant.WhatsAppInstanceName,
                IsActive = tenant.IsActive
            }
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Localizar usuário pelo e-mail ignorando query filters de tenant
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "E-mail ou senha incorretos." });
        }

        if (!user.IsActive)
        {
            return StatusCode(403, new { message = "Seu usuário está desativado. Entre em contato com o suporte." });
        }

        var tenant = user.Tenant;
        if (tenant == null || !tenant.IsActive)
        {
            return StatusCode(403, new { message = "A conta da sua empresa está inativa. Entre em contato com o suporte." });
        }

        string token = _jwtTokenService.GenerateToken(user, tenant);

        _logger.LogInformation("Login bem-sucedido: Usuário '{Email}' no Tenant '{TenantName}'.", user.Email, tenant.Name);

        return Ok(new AuthResponseDto
        {
            Token = token,
            User = new UserSummaryDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                TenantId = user.TenantId
            },
            Tenant = new TenantSummaryDto
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Slug = tenant.Slug,
                BusinessType = tenant.BusinessType,
                Plan = tenant.Plan,
                WhatsAppInstanceName = tenant.WhatsAppInstanceName,
                IsActive = tenant.IsActive
            }
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthResponseDto>> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || user.Tenant == null)
        {
            return NotFound(new { message = "Usuário não localizado." });
        }

        return Ok(new AuthResponseDto
        {
            Token = string.Empty, // Token atual já é válido
            User = new UserSummaryDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                TenantId = user.TenantId
            },
            Tenant = new TenantSummaryDto
            {
                Id = user.Tenant.Id,
                Name = user.Tenant.Name,
                Slug = user.Tenant.Slug,
                BusinessType = user.Tenant.BusinessType,
                Plan = user.Tenant.Plan,
                WhatsAppInstanceName = user.Tenant.WhatsAppInstanceName,
                IsActive = user.Tenant.IsActive
            }
        });
    }

    private static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "loja";
        string str = text.ToLowerInvariant().Trim();
        str = Regex.Replace(str, @"\s+", "-");
        str = Regex.Replace(str, @"[^a-z0-9\-]", "");
        str = Regex.Replace(str, @"\-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(str) ? "loja" : str;
    }
}
