using System.Text.Json.Serialization;

namespace XerifeBot.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommunicationChannel
{
    WhatsApp = 0,
    Instagram = 1
}
