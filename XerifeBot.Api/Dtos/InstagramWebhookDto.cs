using System.Text.Json.Serialization;

namespace XerifeBot.Api.Dtos;

public class InstagramWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<InstagramEntry>? Entry { get; set; }
}

public class InstagramEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("time")]
    public long? Time { get; set; }

    [JsonPropertyName("messaging")]
    public List<InstagramMessagingEvent>? Messaging { get; set; }

    [JsonPropertyName("changes")]
    public List<InstagramChange>? Changes { get; set; }
}

public class InstagramMessagingEvent
{
    [JsonPropertyName("sender")]
    public InstagramEntityId? Sender { get; set; }

    [JsonPropertyName("recipient")]
    public InstagramEntityId? Recipient { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("message")]
    public InstagramMessageDetail? Message { get; set; }

    [JsonPropertyName("postback")]
    public InstagramPostbackDetail? Postback { get; set; }
}

public class InstagramEntityId
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public class InstagramMessageDetail
{
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("is_echo")]
    public bool IsEcho { get; set; } = false;

    [JsonPropertyName("app_id")]
    public long? AppId { get; set; }

    [JsonPropertyName("quick_reply")]
    public InstagramQuickReplyDetail? QuickReply { get; set; }
}

public class InstagramQuickReplyDetail
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

public class InstagramPostbackDetail
{
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

public class InstagramChange
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public InstagramCommentChangeValue? Value { get; set; }
}

public class InstagramCommentChangeValue
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("from")]
    public InstagramUserDetail? From { get; set; }

    [JsonPropertyName("media")]
    public InstagramMediaDetail? Media { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }
}

public class InstagramUserDetail
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

public class InstagramMediaDetail
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("media_product_type")]
    public string? MediaProductType { get; set; }
}
