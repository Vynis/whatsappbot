using System.Text.Json.Serialization;

namespace Kora.Api.Dtos;

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

        // 1. Mensagem enviada diretamente a partir de um item de Produto do Catálogo
        var p = Message.ProductMessage?.Product;
        if (p != null)
        {
            string code = !string.IsNullOrWhiteSpace(p.RetailerId) ? p.RetailerId : p.ProductId ?? "";
            return $"Olá! Tenho interesse no jogo do catálogo: {p.Title} (Código: {code})".Trim();
        }

        // 2. Pedido / Carrinho enviado pelo cliente
        if (Message.OrderMessage != null)
        {
            return $"Olá! Enviei um pedido pelo catálogo com {Message.OrderMessage.ItemCount} item(ns).".Trim();
        }

        // 3. Mensagem com citação de produto (cliente clicou em 'Conversar com a empresa' na tela do produto)
        string? quotedProductPrefix = null;
        var quotedProduct = Message.ExtendedTextMessage?.ContextInfo?.QuotedMessage?.ProductMessage?.Product;
        if (quotedProduct != null)
        {
            string code = !string.IsNullOrWhiteSpace(quotedProduct.RetailerId) ? quotedProduct.RetailerId : quotedProduct.ProductId ?? "";
            quotedProductPrefix = $"[Interesse no produto: {quotedProduct.Title} (Código: {code})] ";
        }

        if (Message.ExtendedTextMessage != null && !string.IsNullOrWhiteSpace(Message.ExtendedTextMessage.Text))
            return (quotedProductPrefix + Message.ExtendedTextMessage.Text).Trim();

        if (!string.IsNullOrWhiteSpace(Message.Conversation))
            return Message.Conversation.Trim();

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

    [JsonPropertyName("productMessage")]
    public ProductMessageContent? ProductMessage { get; set; }

    [JsonPropertyName("orderMessage")]
    public OrderMessageContent? OrderMessage { get; set; }
}

public class ExtendedTextMessageContent
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("contextInfo")]
    public ContextInfoContent? ContextInfo { get; set; }
}

public class ContextInfoContent
{
    [JsonPropertyName("quotedMessage")]
    public EvolutionMessageContent? QuotedMessage { get; set; }
}

public class ProductMessageContent
{
    [JsonPropertyName("product")]
    public ProductDetailsContent? Product { get; set; }

    [JsonPropertyName("businessOwnerJid")]
    public string? BusinessOwnerJid { get; set; }
}

public class ProductDetailsContent
{
    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("retailerId")]
    public string? RetailerId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class OrderMessageContent
{
    [JsonPropertyName("itemCount")]
    public int? ItemCount { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ImageMessageContent
{
    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
