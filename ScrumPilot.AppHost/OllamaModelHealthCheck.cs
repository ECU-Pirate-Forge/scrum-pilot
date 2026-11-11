using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ScrumPilot.AppHost;

// Host GPU service only: checking availability never pulls a model or starts a container.
public sealed class OllamaModelHealthCheck(Func<CancellationToken, ValueTask<string?>> endpoint,
    Func<CancellationToken, ValueTask<string?>> model) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var endpointValue = await endpoint(cancellationToken);
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var address))
                return HealthCheckResult.Degraded("Optional host Ollama endpoint is not configured.");
            using var response = await client.GetAsync(new Uri(address, "/api/tags"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var configuredModel = await model(cancellationToken);
            var available = json.RootElement.GetProperty("models").EnumerateArray()
                .Any(item => item.GetProperty("name").GetString() == configuredModel);
            return available ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("Configured model is not installed.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return HealthCheckResult.Degraded("Optional host Ollama service is unavailable.");
        }
    }
}
