using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PbiController : ControllerBase
    {
        private readonly IPbiService _pbiService;

        public PbiController(IPbiService pbiService)
        {
            _pbiService = pbiService;
        }


        [HttpGet("getAllPbis")]
        public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetAllPbis()
        {
            var pbis = await _pbiService.GetAllPbisAsync();
            return Ok(pbis);
        }

        //[HttpGet("getActivePbis")]
        //public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetActivePbis(int epicId)
        //{
        //    var pbis = await _pbiService.GetActivePbisAsync(epicId);
        //    return Ok(pbis);
        //}

        [HttpGet("getNonDraftPbis")]
        public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetNonDraftPbis(
            [FromQuery] int? sprintId, [FromQuery] int? epicId, [FromQuery] int? projectId)
        {
            IEnumerable<ProductBacklogItem> pbis;

            if (sprintId.HasValue || epicId.HasValue || projectId.HasValue)
            {
                pbis = await _pbiService.GetFilteredPbisAsync(sprintId, epicId, projectId);
            }
            else
            {
                pbis = await _pbiService.GetNonDraftPbisAsync();
            }

            return Ok(pbis);
        }

        [HttpGet("getDraftPbis")]
        public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetDraftPbis()
        {
            var draftPbis = await _pbiService.GetDraftPbisAsync();
            return Ok(draftPbis);
        }

        [HttpPost("generateAiPbis")]
        public async Task<ActionResult<List<ProductBacklogItem>>> GenerateAiPbis([FromBody] List<string> problemStatements)
        {
            if (problemStatements == null || problemStatements.Count == 0)
            {
                return BadRequest("At least one problem statement is required.");
            }

            if (problemStatements.Any(ps => string.IsNullOrWhiteSpace(ps)))
            {
                return BadRequest("All problem statements must be non-empty strings.");
            }

            try
            {
                var pbi = await _pbiService.GenerateAiPbis(problemStatements);

                return Ok(pbi);
            }

            catch (InvalidOperationException ex)
            {
                return BadRequest($"Failed to generate AI story: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, $"Failed to communicate with Ollama service: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                return StatusCode(408, $"Request timed out: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An unexpected error occurred: {ex.Message}");
            }
        }

        [HttpPost("ImprovePbi")]
        public async Task<ActionResult<List<ProductBacklogItem>>> ImprovePbi([FromBody] ProductBacklogItem pbi)
        {
            var improvedPbi = await _pbiService.ImprovePbiAsync(pbi);
            return Ok(improvedPbi);
        }

        [HttpPost("createStory")]
        public async Task<ActionResult<ProductBacklogItem>> CreatePbi([FromBody] ProductBacklogItem pbi)
        {
            pbi.Origin = PbiOrigin.WebUserCreated;
            var created = await _pbiService.CreatePbiAsync(pbi);
            return Ok(created);
        }

        [HttpPost("commitPbi")]
        public async Task<ActionResult<ProductBacklogItem>> CommitPbi([FromBody] ProductBacklogItem pbi)
        {
            try
            {
                var committed = await _pbiService.CommitPbiAsync(pbi);
                return Ok(committed);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("createStories")]
        public async Task<ActionResult<List<ProductBacklogItem>>> CreatePbis([FromBody] List<ProductBacklogItem> pbis)
        {
            var created = new List<ProductBacklogItem>();
            foreach (var pbi in pbis)
            {
                pbi.Origin = PbiOrigin.AiGenerated;
                created.Add(await _pbiService.CreatePbiAsync(pbi));
            }
            return Ok(created);
        }

        [HttpPost("createDraftPbi")]
        public async Task<ActionResult<ProductBacklogItem>> CreateDraftPbi([FromBody] ProductBacklogItem pbi)
        {
            pbi.Origin = PbiOrigin.WebUserCreated;
            var created = await _pbiService.CreateDraftPbiAsync(pbi);
            return Ok(created);
        }

        [HttpPost("createDraftPbis")]
        public async Task<ActionResult<List<ProductBacklogItem>>> CreateDraftPbis([FromBody] List<ProductBacklogItem> pbis)
        {
            var created = new List<ProductBacklogItem>();
            foreach (var pbi in pbis)
            {
                pbi.Origin = PbiOrigin.AiGenerated;
                created.Add(await _pbiService.CreateDraftPbiAsync(pbi));
            }
            return Ok(created);
        }

        [HttpPut]
        public async Task<ActionResult<ProductBacklogItem>> UpdatePbi([FromBody] ProductBacklogItem pbi)
        {
            var updated = await _pbiService.UpdatePbiAsync(pbi);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeletePbi(int id)
        {
            var success = await _pbiService.DeletePbiAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost("addAudioTranscript")]
        public async Task<ActionResult> AddAudioTranscript([FromBody] AudioTranscript transcript)
        {
            //TODO: Implement logic to add transcript to DB
            return Ok();
        }

        [HttpPost("addMessageTranscript")]
        public async Task<ActionResult> AddMessageTranscript([FromBody] MessageTranscript transcript)
        {
            //TODO: Implement logic to add transcript to DB
            return Ok();
        }
    }
}