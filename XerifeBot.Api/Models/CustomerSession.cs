using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

[Table("CustomerSessions")]
public class CustomerSession : IMustHaveTenant
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid TenantId { get; set; }

    /// <summary>
    /// No WhatsApp armazena o JID (ex: 556299999999@s.whatsapp.net).
    /// No Instagram armazena o Instagram Scoped ID (IGSID) do usuário.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string RemoteJid { get; set; } = string.Empty;

    /// <summary>
    /// Canal de comunicação de origem da sessão (WhatsApp ou Instagram)
    /// </summary>
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.WhatsApp;

    [MaxLength(150)]
    public string? PushName { get; set; }

    public bool IsBotPaused { get; set; } = false;

    public DateTime? BotPausedUntil { get; set; }

    [MaxLength(255)]
    public string? PauseReason { get; set; }

    public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public bool IsCurrentlyPaused => IsBotPaused && BotPausedUntil.HasValue && BotPausedUntil.Value > DateTime.UtcNow;
}
