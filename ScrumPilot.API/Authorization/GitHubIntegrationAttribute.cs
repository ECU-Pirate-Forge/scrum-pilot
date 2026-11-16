using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class GitHubIntegrationAttribute : Attribute;

public sealed class GitHubIntegrationMiddleware(RequestDelegate next, ILogger<GitHubIntegrationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, IOptions<FeatureOptions> features)
    {
        if (!features.Value.GitHubIntegration &&
            http.GetEndpoint()?.Metadata.GetMetadata<GitHubIntegrationAttribute>() is not null)
        {
            logger.LogInformation("Rejected a request for the disabled GitHub integration");
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            await http.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "GitHub integration is disabled."
            }, http.RequestAborted);
            return;
        }
        await next(http);
    }
}
