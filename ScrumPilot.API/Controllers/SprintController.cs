using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    /// <summary>
    /// Manages Sprint resources.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SprintController : ControllerBase
    {
        private readonly ISprintService _sprintService;

        /// <summary>Initialises a new instance of <see cref="SprintController"/>.</summary>
        public SprintController(ISprintService sprintService)
        {
            _sprintService = sprintService;
        }

        /// <summary>Returns all sprints, optionally filtered by project.</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Sprint>>> GetAllSprints([FromQuery] int? projectId = null)
        {
            var sprints = projectId.HasValue
                ? await _sprintService.GetSprintsByProjectAsync(projectId.Value)
                : await _sprintService.GetAllSprintsAsync();
            return Ok(sprints);
        }

        /// <summary>Creates a new sprint.</summary>
        [HttpPost]
        public async Task<ActionResult<Sprint>> Create([FromBody] Sprint sprint)
        {
            var created = await _sprintService.CreateAsync(sprint);
            return Ok(created);
        }

        /// <summary>Updates an existing sprint. Returns 400 if the route ID does not match the body.</summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<Sprint>> Update(int id, [FromBody] Sprint sprint)
        {
            if (id != sprint.SprintId) return BadRequest();
            var updated = await _sprintService.UpdateAsync(sprint);
            return Ok(updated);
        }

        /// <summary>Deletes the sprint and unassigns all its PBIs.</summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await _sprintService.DeleteAsync(id);
            return NoContent();
        }
    }
}
