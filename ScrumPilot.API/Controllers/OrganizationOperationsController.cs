using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.API.Services;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/organizations/{organizationId:int}/operations")]
public sealed class OrganizationOperationsController(ControlPlaneContext control, TenantOperations operations,
    IPlanningPokerConnectionEvictor evictor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status(int organizationId, CancellationToken ct)
    {
        var org = await control.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        return org is null ? NotFound() : Ok(new { organizationId, state = org.StorageState, operationId = org.OperationId, error = org.OperationError });
    }
    [HttpPost("retry")]
    public async Task<IActionResult> Retry(int organizationId, CancellationToken ct)
    {
        if (!await control.Organizations.AnyAsync(x => x.OrganizationId == organizationId && x.DeletedAt == null
            && (x.StorageState == "Failed" || x.StorageState == "Provisioning" || x.StorageState == "Active"), ct)) return NotFound();
        await operations.ProvisionAsync(organizationId, ct);
        return await Status(organizationId, ct);
    }
    [HttpGet("backups")]
    public async Task<IActionResult> Backups(int organizationId, CancellationToken ct) =>
        Ok(await control.TenantBackups.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.BackupId, x.CreatedAt }).ToListAsync(ct));
    [HttpPost("backup")]
    public async Task<IActionResult> Backup(int organizationId, CancellationToken ct) =>
        Ok(new { backupId = await operations.BackupAsync(organizationId, ct) });
    [HttpPost("restore/{backupId}")]
    public async Task<IActionResult> Restore(int organizationId, string backupId, CancellationToken ct)
    {
        await operations.RestoreAsync(organizationId, backupId, ct,
            cancellation => evictor.EvictOrganizationAsync(organizationId, cancellation));
        return await Status(organizationId, ct);
    }
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade(int organizationId, CancellationToken ct)
    {
        await operations.UpgradeAsync(organizationId, ct,
            cancellation => evictor.EvictOrganizationAsync(organizationId, cancellation));
        return await Status(organizationId, ct);
    }
}
