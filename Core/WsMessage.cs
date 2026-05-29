using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotWorker.Domain;

public record WsMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] JsonElement? Data = null
);
