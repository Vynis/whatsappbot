using System.ComponentModel.DataAnnotations;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Dtos;

public class RegisterRequest
{
    [Required(ErrorMessage = "O nome da empresa ou loja é obrigatório.")]
    [StringLength(150, MinimumLength = 2)]
    public string CompanyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "O nome do responsável é obrigatório.")]
    [StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "A senha é obrigatória.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter no mínimo 6 caracteres.")]
    public string Password { get; set; } = string.Empty;

    public BusinessType BusinessType { get; set; } = BusinessType.Rental;

    public string? CityOrRegion { get; set; }

    public string? WebsiteUrl { get; set; }
}

public class LoginRequest
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "A senha é obrigatória.")]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public UserSummaryDto User { get; set; } = null!;
    public TenantSummaryDto Tenant { get; set; } = null!;
}

public class UserSummaryDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}

public class TenantSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public BusinessType BusinessType { get; set; }
    public string Plan { get; set; } = "Free";
    public string? WhatsAppInstanceName { get; set; }
    public bool IsActive { get; set; }
}
