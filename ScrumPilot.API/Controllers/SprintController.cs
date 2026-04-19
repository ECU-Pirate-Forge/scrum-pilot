using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SprintController : ControllerBase
    {
        private readonly ISprintService _sprintService;

        public SprintController(ISprintService sprintService)
        {
            _sprintService = sprintService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Sprint>>> GetAllSprints([FromQuery] int? projectId = null)
        {
            var sprints = projectId.HasValue
                ? await _sprintService.GetSprintsByProjectAsync(projectId.Value)
                : await _sprintService.GetAllSprintsAsync();
            return Ok(sprints);
        }
    }
}
