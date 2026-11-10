using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

[Table("MessageLogs")]
public class MessageLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string RemoteJid { get; set; } = string.Empty;

    [Required]
    public MessageSender Sender { get; set; }

    [Column(TypeName = "text")]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
