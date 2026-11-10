using System.Text.Json.Serialization;

namespace XerifeBot.Api.Dtos;

public class EvolutionSendTextRequest
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("delay")]
    public int Delay { get; set; } = 2500;

    [JsonPropertyName("linkPreview")]
    public bool LinkPreview { get; set; } = true;
}

public class EvolutionSendMediaRequest
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("mediatype")]
    public string MediaType { get; set; } = "image";

    [JsonPropertyName("media")]
    public string Media { get; set; } = string.Empty;

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("delay")]
    public int Delay { get; set; } = 2000;
}

public class EvolutionSendResponse
{
    [JsonPropertyName("key")]
    public EvolutionKey? Key { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
