using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XerifeBot.Api.Models;

[Table("BoardGames")]
public class BoardGame
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string? BoardgameId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = "Geral";

    public int MinPlayers { get; set; } = 2;

    public int MaxPlayers { get; set; } = 4;

    [Column(TypeName = "decimal(10,2)")]
    public decimal? AquisitionPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RentalPrice { get; set; }

    public int? IdBgg { get; set; }

    [MaxLength(300)]
    public string? LinkLudopedia { get; set; }

    [MaxLength(100)]
    public string? CopyName { get; set; }

    [MaxLength(150)]
    public string? Slug { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(500)]
    public string? Comments { get; set; }

    public bool IsAvailable { get; set; } = true;
}
