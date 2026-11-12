using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

[Table("TenantSettings")]
public class TenantSettings : IMustHaveTenant
{
    [Key]
    public Guid TenantId { get; set; }

    [MaxLength(100)]
    public string BotName { get; set; } = "Atendente";

    [MaxLength(1000)]
    public string? WelcomeMessage { get; set; }

    [MaxLength(300)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(150)]
    public string? CityOrRegion { get; set; }

    public bool IsBotActiveGlobally { get; set; } = true;

    public bool IsAiEnabled { get; set; } = false;

    [MaxLength(50)]
    public string AiProvider { get; set; } = "gemini"; // "gemini" | "openai"

    [MaxLength(255)]
    public string? AiApiKey { get; set; }

    [MaxLength(100)]
    public string AiModel { get; set; } = "gemini-flash-lite-latest";

    public string? AiSystemPrompt { get; set; }

    public DateTime? LastCatalogSyncAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TenantId))]
    public Tenant? Tenant { get; set; }
}
