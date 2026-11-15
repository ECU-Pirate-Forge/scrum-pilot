using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;

namespace ScrumPilot.API.Services;

public sealed class GitHubCheckRecovery(ControlPlaneContext control, GitHubAppClient github)
{
    public async Task InvalidateAsync(GitHubRepositoryRegistration registration, string reason, CancellationToken ct)
    {
        var heads = await control.PullRequestScopeAttempts.AsNoTracking()
            .Where(item => item.RegistrationId == registration.RegistrationId)
            .Select(item => item.HeadSha).Distinct().ToListAsync(ct);
        foreach (var head in heads)
            await github.PublishCheckAsync(registration.InstallationId, registration.GitHubRepositoryId,
                registration.Owner, registration.Name, head, false, reason, ct);
    }
}
