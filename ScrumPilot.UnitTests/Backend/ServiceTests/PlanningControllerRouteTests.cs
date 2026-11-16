using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PlanningControllerRouteTests
{
    [Theory]
    [InlineData("GET", "/api/organizations/1/projects/1/repository")]
    [InlineData("PUT", "/api/organizations/1/projects/1/repository")]
    [InlineData("GET", "/api/organizations/1/projects/1/repository/snapshots")]
    [InlineData("POST", "/api/organizations/1/projects/1/repository/snapshots")]
    [InlineData("POST", "/api/organizations/1/projects/1/task-generation/preview")]
    [InlineData("POST", "/api/organizations/1/projects/1/task-generation/accept")]
    [InlineData("GET", "/api/organizations/1/projects/1/tasks/1/specifications")]
    [InlineData("POST", "/api/organizations/1/projects/1/tasks/1/specifications")]
    [InlineData("POST", "/api/organizations/1/projects/1/tasks/1/specifications/approve")]
    [InlineData("GET", "/api/organizations/1/projects/1/tasks/1/pull-requests")]
    [InlineData("POST", "/api/organizations/1/projects/1/tasks/1/pull-requests")]
    [InlineData("POST", "/api/organizations/1/projects/1/repository/non-task-exceptions")]
    [InlineData("POST", "/api/integrations/github/webhooks")]
    public async Task DisabledIntegrationRejectsRepositoryPlanningBeforeAuthenticationOrStorage(string method, string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<FeatureOptions>(_ => { });
        builder.Services.AddControllers().AddApplicationPart(typeof(ProjectRepositoryController).Assembly);
        builder.Logging.ClearProviders();
        await using var application = builder.Build();
        application.UseRouting();
        application.UseMiddleware<GitHubIntegrationMiddleware>();
        application.Use((HttpContext http, RequestDelegate _) =>
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        application.MapControllers();
        await application.StartAsync();
        using var client = application.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("GitHub integration is disabled.", problem?.Title);
        using var ordinaryPlanning = await client.GetAsync("/api/organizations/1/projects/1/problem-statements");
        Assert.Equal(HttpStatusCode.Unauthorized, ordinaryPlanning.StatusCode);
    }

    [Theory]
    [InlineData(typeof(ProjectGuidelinesController))]
    [InlineData(typeof(ProjectProblemStatementsController))]
    [InlineData(typeof(ProjectStoryGenerationController))]
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
}
