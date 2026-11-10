using XerifeBot.Api.Models;

namespace XerifeBot.Api.Dtos;

public class CustomerSessionDto
{
    public int Id { get; set; }
    public string RemoteJid { get; set; } = string.Empty;
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.WhatsApp;
    public string? PushName { get; set; }
    public string FormattedPhone { get; set; } = string.Empty;
    public bool IsBotPaused { get; set; }
    public DateTime? BotPausedUntil { get; set; }
    public string? PauseReason { get; set; }
    public DateTime LastInteractionAt { get; set; }
    public bool IsCurrentlyPaused { get; set; }
}

public class BoardGameDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public decimal RentalPrice { get; set; }
    public decimal? AquisitionPrice { get; set; }
    public string? LinkLudopedia { get; set; }
    public string? CopyName { get; set; }
    public string? Slug { get; set; }
    public string? Status { get; set; }
    public string? Comments { get; set; }
    public bool IsAvailable { get; set; }
}

public class UploadCatalogResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int GamesImported { get; set; }
    public int GamesAvailable { get; set; }
    public int GamesRented { get; set; }
    public List<string> ImportedNames { get; set; } = new();
}

public class TogglePauseRequest
{
    public int? DurationHours { get; set; } = 12;
    public string? Reason { get; set; }
}

public class CloseSessionRequest
{
    public bool SendGoodbyeMessage { get; set; } = true;
    public string? CustomMessage { get; set; }
}

public class InstanceInfoDto
{
    public string InstanceName { get; set; } = string.Empty;
    public string Status { get; set; } = "close";
    public string? OwnerJid { get; set; }
    public string? ProfileName { get; set; }
}

public class WhatsAppStatusDto
{
    public string InstanceName { get; set; } = string.Empty;
    public string Status { get; set; } = "close";
    public bool IsConnected { get; set; }
    public string? OwnerJid { get; set; }
    public string FormattedPhone { get; set; } = string.Empty;
    public string? ProfileName { get; set; }
}

public class InstagramCommentLogDto
{
    public int Id { get; set; }
    public string CommentId { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string? PublicReplyText { get; set; }
    public bool PrivateDmSent { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SetupIceBreakersResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
