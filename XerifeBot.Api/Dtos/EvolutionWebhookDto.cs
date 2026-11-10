using System.Text.Json.Serialization;

namespace XerifeBot.Api.Dtos;

public class EvolutionWebhookDto
{
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }

    [JsonPropertyName("data")]
    public EvolutionMessageData? Data { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }
}

public class EvolutionMessageData
{
    [JsonPropertyName("key")]
    public EvolutionKey? Key { get; set; }

    [JsonPropertyName("pushName")]
    public string? PushName { get; set; }

    [JsonPropertyName("messageType")]
    public string? MessageType { get; set; }

    [JsonPropertyName("message")]
    public EvolutionMessageContent? Message { get; set; }

    [JsonPropertyName("messageTimestamp")]
    public long? MessageTimestamp { get; set; }

    public string? ExtractText()
    {
        if (Message == null) return null;

        if (!string.IsNullOrWhiteSpace(Message.Conversation))
            return Message.Conversation.Trim();

        if (Message.ExtendedTextMessage != null && !string.IsNullOrWhiteSpace(Message.ExtendedTextMessage.Text))
            return Message.ExtendedTextMessage.Text.Trim();

        if (Message.ImageMessage != null && !string.IsNullOrWhiteSpace(Message.ImageMessage.Caption))
            return Message.ImageMessage.Caption.Trim();

        return null;
    }
}

public class EvolutionKey
{
    [JsonPropertyName("remoteJid")]
    public string? RemoteJid { get; set; }

    [JsonPropertyName("fromMe")]
    public bool FromMe { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("participant")]
    public string? Participant { get; set; }
}

public class EvolutionMessageContent
{
    [JsonPropertyName("conversation")]
    public string? Conversation { get; set; }

    [JsonPropertyName("extendedTextMessage")]
    public ExtendedTextMessageContent? ExtendedTextMessage { get; set; }

    [JsonPropertyName("imageMessage")]
    public ImageMessageContent? ImageMessage { get; set; }
}

public class ExtendedTextMessageContent
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class ImageMessageContent
{
    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
