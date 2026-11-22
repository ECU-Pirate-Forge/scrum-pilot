using System.Text.Json;

namespace ScrumPilot.API.Services.AI;

internal static class StructuredJsonResponse
{
    internal static T Deserialize<T>(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            throw new JsonException("The AI response was empty.");

        var json = responseText.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('\n');
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                json = json[(start + 1)..end].Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new JsonException("The AI response deserialized to null.");
        }
        catch (JsonException)
        {
            throw;
        }
    }

    internal static T Deserialize<T>(string? responseText, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            throw new JsonException("The AI response was empty.");

        var json = responseText.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('\n');
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                json = json[(start + 1)..end].Trim();
        }

        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new JsonException("The AI response deserialized to null.");
    }
}
