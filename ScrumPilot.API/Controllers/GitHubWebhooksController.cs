using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[GitHubIntegration]
[Route("api/integrations/github/webhooks")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class GitHubWebhooksController(GitHubAppClient github, ControlPlaneContext control,
    GitHubRepositoryRegistry registry, IServiceScopeFactory scopes, PlanningIntegrationLifecycle lifecycle) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (!github.VerifyWebhook(body, Request.Headers["X-Hub-Signature-256"].ToString())) return Unauthorized();
        if (!Guid.TryParse(Request.Headers["X-GitHub-Delivery"], out var delivery)) return BadRequest("A valid delivery ID is required.");
        var deliveryId = delivery.ToString("D");
        if (await control.GitHubWebhookDeliveries.AnyAsync(item => item.DeliveryId == deliveryId, ct)) return NoContent();
        if (Request.Headers["X-GitHub-Event"] == "ping") return Ok();
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(body); }
        catch (JsonException) { return BadRequest("A valid JSON object is required."); }
        using var payload = parsed;
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return BadRequest("A JSON object is required.");
        var eventName = Request.Headers["X-GitHub-Event"].ToString();
        if (eventName is "installation" or "installation_repositories")
        {
            if (!root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("installation", out var changed) || changed.ValueKind != JsonValueKind.Object
                || !changed.TryGetProperty("id", out var changedId) || changedId.ValueKind != JsonValueKind.Number
                || !changedId.TryGetInt64(out var changedInstallationId)
                || changedInstallationId <= 0)
                return BadRequest("Incomplete installation event.");
            if (eventName == "installation" && action.GetString() is "deleted" or "suspend")
                await lifecycle.SuspendInstallationAsync(changedInstallationId, null, ct);
            else if (eventName == "installation_repositories" && action.GetString() == "removed")
            {
                if (!root.TryGetProperty("repositories_removed", out var removed) || removed.ValueKind != JsonValueKind.Array)
                    return BadRequest("Removed repositories are required.");
                var ids = new List<long>();
                foreach (var item in removed.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id)
                        || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var value) || value <= 0)
                        return BadRequest("A positive repository ID is required.");
                    ids.Add(value);
                }
                await lifecycle.SuspendInstallationAsync(changedInstallationId, ids, ct);
            }
            await RecordDeliveryAsync(deliveryId, ct);
            return NoContent();
        }
        if (eventName != "pull_request") return NoContent();
        if (!root.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("installation", out var installation) || installation.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number
            || !repository.TryGetProperty("id", out var repositoryValue) || repositoryValue.ValueKind != JsonValueKind.Number
            || !repositoryValue.TryGetInt64(out var repositoryId)
            || !installation.TryGetProperty("id", out var installationValue) || installationValue.ValueKind != JsonValueKind.Number
            || !installationValue.TryGetInt64(out var installationId)
            || !number.TryGetInt32(out var pullRequestNumber) || repositoryId <= 0 || installationId <= 0 || pullRequestNumber <= 0)
            return BadRequest("Incomplete pull-request event.");
        var registration = await registry.ResolveAsync(repositoryId, installationId, ct);
        await using var tenantScope = scopes.CreateAsyncScope();
        tenantScope.ServiceProvider.GetRequiredService<TenantSelection>().OrganizationId = registration.OrganizationId;
        await tenantScope.ServiceProvider.GetRequiredService<PullRequestScopeService>()
            .ProcessWebhookAsync(repositoryId, installationId, pullRequestNumber, ct);
        await RecordDeliveryAsync(deliveryId, ct);
        return NoContent();
    }

    private async Task RecordDeliveryAsync(string deliveryId, CancellationToken ct)
    {
        control.GitHubWebhookDeliveries.Add(new() { DeliveryId = deliveryId });
        try { await control.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            if (!await control.GitHubWebhookDeliveries.AsNoTracking().AnyAsync(item => item.DeliveryId == deliveryId, ct)) throw;
        }
    }
}
