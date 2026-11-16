using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Controllers;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PlanningControllerRouteTests
{
    [Theory]
    [InlineData(typeof(ProjectGuidelinesController))]
    [InlineData(typeof(ProjectProblemStatementsController))]
    [InlineData(typeof(ProjectStoryGenerationController))]
    [InlineData(typeof(TaskCreationController))]
    [InlineData(typeof(TaskGenerationController))]
    [InlineData(typeof(TaskSpecificationsController))]
    [InlineData(typeof(DependenciesController))]
    public void PlanningRoutesRequireExplicitOrganizationAndProject(Type controller)
    {
        Assert.NotNull(controller.GetCustomAttribute<OrganizationWorkAttribute>(inherit: true));
        var routes = controller.GetCustomAttributes<RouteAttribute>(inherit: true).ToArray();
        Assert.NotEmpty(routes);
        Assert.All(routes, route =>
            Assert.StartsWith("api/organizations/{organizationId:int}/projects/{projectId:int}", route.Template));
    }

    [Fact]
    public void LegacyPbiControllerRetainsOrganizationWorkBoundary()
    {
        Assert.NotNull(typeof(PbiController).GetCustomAttribute<OrganizationWorkAttribute>(inherit: true));
        Assert.All(typeof(PbiController).GetCustomAttributes<RouteAttribute>(inherit: true),
            route => Assert.StartsWith("api/organizations/{organizationId:int}/", route.Template));
    }

    [Fact]
    public async Task DisabledGitHubFeatureReturnsNotFoundWithoutExecutingAction()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var context = new ActionExecutingContext(actionContext, filters, new Dictionary<string, object?>(), new object());
        var filter = new GitHubFeatureFilter(Options.Create(new GitHubIntegrationOptions { Enabled = false }));
        var nextExecuted = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextExecuted = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, filters, new object()));
        });

        Assert.False(nextExecuted);
        Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status404NotFound, ((ObjectResult)context.Result!).StatusCode);
    }

    [Theory]
    [InlineData(typeof(ProjectRepositoryController))]
    [InlineData(typeof(TaskGenerationController))]
    [InlineData(typeof(TaskSpecificationsController))]
    [InlineData(typeof(TaskPullRequestsController))]
    [InlineData(typeof(GitHubWebhooksController))]
    public void GitHubControllerRoutesUseTheFeatureFilter(Type controller)
    {
        Assert.Contains(controller.GetCustomAttributes<ServiceFilterAttribute>(inherit: true),
            filter => filter.ServiceType == typeof(GitHubFeatureFilter));
    }
}
