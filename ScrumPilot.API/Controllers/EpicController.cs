using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EpicController : ControllerBase
    {
        private readonly IEpicService _epicService;

        public EpicController(IEpicService epicService)
        {
            _epicService = epicService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Epic>>> GetAllEpics([FromQuery] int? projectId = null)
        {
            var epics = projectId.HasValue
                ? await _epicService.GetEpicsByProjectAsync(projectId.Value)
                : await _epicService.GetAllEpicsAsync();
            return Ok(epics);
        }

        [HttpPost]
        public async Task<ActionResult<Epic>> Create([FromBody] Epic epic)
        {
            var created = await _epicService.CreateAsync(epic);
            return Ok(created);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<Epic>> Update(int id, [FromBody] Epic epic)
        {
            if (id != epic.EpicId) return BadRequest();
            var updated = await _epicService.UpdateAsync(epic);
            return Ok(updated);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await _epicService.DeleteAsync(id);
            return NoContent();
        }
    }
}
