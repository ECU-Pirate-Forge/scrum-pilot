using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/features")]
public sealed class FeatureController(IOptions<FeatureOptions> options) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { GitHubIntegrationEnabled = options.Value.GitHubIntegration });
}
