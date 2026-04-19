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

        [HttpPost]
        public async Task<ActionResult<Sprint>> Create([FromBody] Sprint sprint)
        {
            var created = await _sprintService.CreateAsync(sprint);
            return Ok(created);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<Sprint>> Update(int id, [FromBody] Sprint sprint)
        {
            if (id != sprint.SprintId) return BadRequest();
            var updated = await _sprintService.UpdateAsync(sprint);
            return Ok(updated);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await _sprintService.DeleteAsync(id);
            return NoContent();
        }
    }
}
