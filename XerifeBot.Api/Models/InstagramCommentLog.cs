using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

[Table("InstagramCommentLogs")]
public class InstagramCommentLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string CommentId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MediaId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string CommentText { get; set; } = string.Empty;

    public string? PublicReplyText { get; set; }

    public bool PrivateDmSent { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
