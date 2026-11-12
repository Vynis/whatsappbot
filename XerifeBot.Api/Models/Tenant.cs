using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

public enum BusinessType
{
    Rental = 1, // Locadora de Jogos
    Store = 2,  // Loja / Venda de Jogos
    Hybrid = 3  // Híbrido: Locadora e Venda
}

[Table("Tenants")]
public class Tenant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string Slug { get; set; } = string.Empty;

    public BusinessType BusinessType { get; set; } = BusinessType.Rental;

    [MaxLength(100)]
    public string? WhatsAppInstanceName { get; set; }

    [MaxLength(100)]
    public string? InstagramAccountId { get; set; }

    [MaxLength(500)]
    public string? InstagramAccessToken { get; set; }

    [MaxLength(100)]
    public string? InstagramVerifyToken { get; set; }

    [MaxLength(50)]
    public string Plan { get; set; } = "Free";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TenantSettings? Settings { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
}
