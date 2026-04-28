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
        public async Task<ActionResult<IEnumerable<Epic>>> GetAllEpics()
        {
            var epics = await _epicService.GetAllEpicsAsync();
            return Ok(epics);
        }
    }
}
