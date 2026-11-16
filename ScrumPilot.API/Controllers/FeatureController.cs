using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/features")]
public sealed class FeatureController(IOptions<GitHubIntegrationOptions> options) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { GitHubIntegrationEnabled = options.Value.Enabled });
}
