using System.Text.Json.Serialization;

namespace Kora.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommunicationChannel
{
    WhatsApp = 0,
    Instagram = 1
}
