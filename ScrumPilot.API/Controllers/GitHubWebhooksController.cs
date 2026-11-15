using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/integrations/github/webhooks")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class GitHubWebhooksController(GitHubAppClient github, PullRequestScopeService scope, ScrumPilotContext db) : ControllerBase
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
        if (await db.GitHubWebhookDeliveries.AnyAsync(item => item.DeliveryId == deliveryId, ct)) return NoContent();
        if (Request.Headers["X-GitHub-Event"] == "ping") return Ok();
        if (Request.Headers["X-GitHub-Event"] != "pull_request") return NoContent();
        using var payload = JsonDocument.Parse(body);
        var root = payload.RootElement;
        if (!root.TryGetProperty("repository", out var repository) || !root.TryGetProperty("installation", out var installation)
            || !root.TryGetProperty("number", out var number)) return BadRequest("Incomplete pull-request event.");
        await scope.ProcessWebhookAsync(repository.GetProperty("id").GetInt64(), installation.GetProperty("id").GetInt64(), number.GetInt32(), ct);
        db.GitHubWebhookDeliveries.Add(new() { DeliveryId = deliveryId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
