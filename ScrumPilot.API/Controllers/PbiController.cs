using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    /// <summary>
    /// Manages Product Backlog Item (PBI) resources including CRUD operations,
    /// draft workflows, and AI story generation.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PbiController : ControllerBase
    {
        private readonly IPbiService _pbiService;

        /// <summary>Initialises a new instance of <see cref="PbiController"/>.</summary>
        public PbiController(IPbiService pbiService)
        {
            _pbiService = pbiService;
        }

        /// <summary>Returns all PBIs regardless of draft status.</summary>
        [HttpGet("getAllPbis")]
        public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetAllPbis()
        {
            var pbis = await _pbiService.GetAllPbisAsync();
            return Ok(pbis);
        }

        /// <summary>
        /// Returns non-draft PBIs, optionally filtered by sprint, epic, and/or project.
        /// Pass <c>sprintId=-1</c> to retrieve PBIs with no sprint assigned.
        /// </summary>
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

        /// <summary>Returns all draft PBIs awaiting review and commit.</summary>
        [HttpGet("getDraftPbis")]
        public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetDraftPbis()
        {
            var draftPbis = await _pbiService.GetDraftPbisAsync();
            return Ok(draftPbis);
        }

        /// <summary>
        /// Calls the configured AI provider for each problem statement and returns
        /// the generated draft PBIs without persisting them.
        /// </summary>
        /// <param name="problemStatements">One or more non-empty problem statements to generate stories from.</param>
        [HttpPost("generateAiPbis")]
        public async Task<ActionResult<List<ProductBacklogItem>>> GenerateAiPbis([FromBody] List<string> problemStatements)
        {
            if (problemStatements == null || problemStatements.Count == 0)
                return BadRequest("At least one problem statement is required.");

            if (problemStatements.Any(ps => string.IsNullOrWhiteSpace(ps)))
                return BadRequest("All problem statements must be non-empty strings.");

            try
            {
                var pbis = await _pbiService.GenerateAiPbis(problemStatements);
                return Ok(pbis);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest($"Failed to generate AI story: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, $"Failed to communicate with AI service: {ex.Message}");
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

        /// <summary>
        /// Rewrites and improves an existing PBI using the configured AI provider.
        /// Returns the improved PBI without persisting changes.
        /// </summary>
        [HttpPost("ImprovePbi")]
        public async Task<ActionResult<ProductBacklogItem>> ImprovePbi([FromBody] ProductBacklogItem pbi)
        {
            var improvedPbi = await _pbiService.ImprovePbiAsync(pbi);
            return Ok(improvedPbi);
        }

        /// <summary>Creates and persists a new non-draft PBI.</summary>
        [HttpPost("createStory")]
        public async Task<ActionResult<ProductBacklogItem>> CreatePbi([FromBody] ProductBacklogItem pbi)
        {
            pbi.Origin = PbiOrigin.WebUserCreated;
            var created = await _pbiService.CreatePbiAsync(pbi);
            return Ok(created);
        }

        /// <summary>Clears the draft flag on the supplied PBI and saves the change.</summary>
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

        /// <summary>Creates and persists multiple non-draft PBIs in a single request.</summary>
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

        /// <summary>Creates and persists a new PBI in draft state.</summary>
        [HttpPost("createDraftPbi")]
        public async Task<ActionResult<ProductBacklogItem>> CreateDraftPbi([FromBody] ProductBacklogItem pbi)
        {
            pbi.Origin = PbiOrigin.WebUserCreated;
            var created = await _pbiService.CreateDraftPbiAsync(pbi);
            return Ok(created);
        }

        /// <summary>Creates and persists multiple draft PBIs in a single request.</summary>
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

        /// <summary>Updates an existing PBI and returns the saved entity.</summary>
        [HttpPut]
        public async Task<ActionResult<ProductBacklogItem>> UpdatePbi([FromBody] ProductBacklogItem pbi)
        {
            var updated = await _pbiService.UpdatePbiAsync(pbi);
            return Ok(updated);
        }

        /// <summary>
        /// Permanently deletes the PBI with the given ID.
        /// Returns 404 if no PBI with that ID exists.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeletePbi(int id)
        {
            var success = await _pbiService.DeletePbiAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
