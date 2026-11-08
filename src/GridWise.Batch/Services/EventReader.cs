using System.Text.Json;
using GridWise.Batch.Events;

namespace GridWise.Batch.Services;

public class PoisonMessageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads the common envelope and its data; anything unreadable is a poison message.</summary>
public static class EventReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EventEnvelope Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EventEnvelope>(json, Json)
                ?? throw new PoisonMessageException("Message is not an event envelope.");
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Message is not valid JSON.", ex);
        }
    }

    public static T ReadData<T>(EventEnvelope envelope) where T : class
    {
        if (envelope.Version != 1)
            throw new PoisonMessageException($"Unsupported version {envelope.Version} of {envelope.Type}.");

        try
        {
            return envelope.Data.Deserialize<T>(Json)
                ?? throw new PoisonMessageException($"{envelope.Type} has no data.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new PoisonMessageException($"{envelope.Type} data cannot be read.", ex);
        }
    }
}
