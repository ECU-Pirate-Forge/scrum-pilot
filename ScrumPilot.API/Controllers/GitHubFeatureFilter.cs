using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;

namespace ScrumPilot.API.Controllers;

public sealed class GitHubFeatureFilter(IOptions<GitHubIntegrationOptions> options) : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!options.Value.Enabled)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "GitHub integration is disabled."
            })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
            return Task.CompletedTask;
        }

        return next();
    }
}
