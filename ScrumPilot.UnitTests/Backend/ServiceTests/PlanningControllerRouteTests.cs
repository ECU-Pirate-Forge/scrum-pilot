using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PlanningControllerRouteTests
{
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
