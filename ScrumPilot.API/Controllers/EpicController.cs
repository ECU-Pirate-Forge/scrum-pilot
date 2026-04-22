using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    /// <summary>
    /// Manages Epic resources.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EpicController : ControllerBase
    {
        private readonly IEpicService _epicService;

        /// <summary>Initialises a new instance of <see cref="EpicController"/>.</summary>
        public EpicController(IEpicService epicService)
        {
            _epicService = epicService;
        }

        /// <summary>Returns all epics, optionally filtered by project.</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Epic>>> GetAllEpics([FromQuery] int? projectId = null)
        {
            var epics = projectId.HasValue
                ? await _epicService.GetEpicsByProjectAsync(projectId.Value)
                : await _epicService.GetAllEpicsAsync();
            return Ok(epics);
        }

        /// <summary>Creates a new epic.</summary>
        [HttpPost]
        public async Task<ActionResult<Epic>> Create([FromBody] Epic epic)
        {
            var created = await _epicService.CreateAsync(epic);
            return Ok(created);
        }

        /// <summary>Updates an existing epic. Returns 400 if the route ID does not match the body.</summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<Epic>> Update(int id, [FromBody] Epic epic)
        {
            if (id != epic.EpicId) return BadRequest();
            var updated = await _epicService.UpdateAsync(epic);
            return Ok(updated);
        }

        /// <summary>Deletes the epic with the given ID.</summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await _epicService.DeleteAsync(id);
            return NoContent();
        }
    }
}
