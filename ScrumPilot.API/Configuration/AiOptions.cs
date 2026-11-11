using System.Data.Common;

namespace ScrumPilot.API.Configuration;

public sealed class AiOptions
{
    public string Model { get; set; } = "gemma4:latest";
    public Uri Endpoint { get; private set; } = new("http://localhost:11434");
    public string ConnectionString => new DbConnectionStringBuilder
    { ["Endpoint"] = Endpoint.AbsoluteUri, ["Model"] = Model }.ConnectionString;
    public static AiOptions FromConfiguration(IConfiguration configuration)
    {
        var value = new AiOptions();
        var connection = configuration.GetConnectionString("ollama");
        var address = "http://localhost:11434";
        if (!string.IsNullOrWhiteSpace(connection))
        {
            if (Uri.TryCreate(connection, UriKind.Absolute, out var direct) && direct.Scheme is "http" or "https")
                address = connection;
            else
            {
                var parsed = new DbConnectionStringBuilder { ConnectionString = connection };
                if (!parsed.TryGetValue("Endpoint", out var configuredEndpoint))
                    throw new InvalidOperationException("ConnectionStrings:ollama requires Endpoint.");
                address = configuredEndpoint.ToString()!;
                if (parsed.TryGetValue("Model", out var model)) value.Model = model.ToString()!;
            }
        }
        value.Model = configuration["AI:Model"] ?? value.Model;
        if (string.IsNullOrWhiteSpace(value.Model)) throw new InvalidOperationException("AI:Model is required.");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new InvalidOperationException("ConnectionStrings:ollama must be an HTTP endpoint without embedded credentials.");
        value.Endpoint = endpoint;
        return value;
    }
}
