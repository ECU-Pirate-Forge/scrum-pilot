using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OrganizationWorkAttribute : Attribute;

public sealed class OrganizationBodyFilter(TenantSelection selection) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<OrganizationWorkAttribute>().Any()
            && context.ActionArguments.Values.Any(Conflicts))
            context.Result = new BadRequestObjectResult("Organization does not match the work route.");
    }
    private bool Conflicts(object? value)
    {
        if (value is null || value is string) return false;
        if (value is System.Collections.IEnumerable list) return list.Cast<object?>().Any(Conflicts);
        var id = value.GetType().GetProperty("OrganizationId")?.GetValue(value);
        return id is int organizationId && organizationId != 0 && organizationId != selection.OrganizationId;
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}

public sealed class OrganizationResolverMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ControlPlaneContext control, TenantSelection selection, TenantStorage storage)
    {
        try
        {
            if (http.GetEndpoint()?.Metadata.GetMetadata<OrganizationWorkAttribute>() != null)
            {
                if (http.User.Identity?.IsAuthenticated != true) { http.Response.StatusCode = 401; return; }
                if (!int.TryParse(http.Request.RouteValues["organizationId"]?.ToString(), out var id) || id <= 0)
                { http.Response.StatusCode = 404; return; }
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                // Membership must be checked before consulting storage, including for global Admins.
                var member = await control.OrganizationMemberships.AsNoTracking().AnyAsync(m =>
                    m.OrganizationId == id && m.UserId == userId && m.Organization!.DeletedAt == null,
                    http.RequestAborted);
                if (!member) { http.Response.StatusCode = 404; return; }
                selection.OrganizationId = id;
                if (http.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true
                    && http.Request.ContentLength != 0)
                {
                    http.Request.EnableBuffering();
                    try
                    {
                        using var body = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted);
                        if (ConflictingOrganization(body.RootElement, id))
                        { http.Response.StatusCode = 400; return; }
                    }
                    catch (JsonException) { http.Response.StatusCode = 400; return; }
                    finally { http.Request.Body.Position = 0; }
                }
                {
                    var org = await control.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == id, http.RequestAborted);
                    using var verification = storage.Open(org);
                }
            }
            await next(http);
        }
        catch (Exception exception) when (exception is TenantUnavailableException or IOException or Microsoft.Data.SqlClient.SqlException)
        {
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = 503;
            await http.Response.WriteAsJsonAsync(new ProblemDetails { Status = 503, Title = "Organization storage is unavailable." });
        }
    }
    private static bool ConflictingOrganization(JsonElement element, int organizationId) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Any(x => ConflictingOrganization(x, organizationId))
        : element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any(p =>
            p.Name.Equals("organizationId", StringComparison.OrdinalIgnoreCase)
                ? p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var id) || id != organizationId
                : ConflictingOrganization(p.Value, organizationId));
}
