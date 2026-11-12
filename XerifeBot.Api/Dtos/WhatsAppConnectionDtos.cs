namespace XerifeBot.Api.Dtos;

public class WhatsAppConnectResultDto
{
    public string InstanceName { get; set; } = string.Empty;
    public string Status { get; set; } = "close";
    public bool IsConnected { get; set; }
    public string? QrCodeBase64 { get; set; }
    public string? PairingCode { get; set; }
    public string? Message { get; set; }
}

public class WhatsAppActionResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Status { get; set; }
}
