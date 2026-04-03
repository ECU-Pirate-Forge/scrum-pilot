using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StoryController : ControllerBase
    {
        private readonly IStoryService _storyService;

        public StoryController(IStoryService storyService)
        {
            _storyService = storyService;
        }

        [HttpGet("getAllStories")]
        public async Task<ActionResult<IEnumerable<Story>>> GetAllStories()
        {
            var stories = await _storyService.GetAllStoriesAsync();
            return Ok(stories);
        }

        [HttpGet("getDraftStories")]
        public async Task<ActionResult<IEnumerable<Story>>> GetDraftStories()
        {
            var draftStories = await _storyService.GetDraftStoriesAsync();
            return Ok(draftStories);
        }

        [HttpPost("generateAiStories")]
        public async Task<ActionResult<List<Story>>> GenerateAiStory([FromBody] List<string> problemStatements)
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
                var story = await _storyService.GenerateAiStory(problemStatements);

                return Ok(story);
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

        [HttpPost("createStory")]
        public async Task<ActionResult<Story>> CreateStory([FromBody] Story story)
        {
            var created = await _storyService.CreateStoryAsync(story);
            return Ok(created);
        }

        [HttpPost("createStories")]
        public async Task<ActionResult<List<Story>>> CreateStories([FromBody] List<Story> stories)
        {
            var created = new List<Story>();
            foreach (var story in stories)
            {
                created.Add(await _storyService.CreateStoryAsync(story));
            }
            return Ok(created);
        }

        [HttpPost("createDraftStory")]
        public async Task<ActionResult<Story>> CreateDraftStory([FromBody] Story story)
        {
            var created = await _storyService.CreateDraftStoryAsync(story);
            return Ok(created);
        }

        [HttpPost("createDraftStories")]
        public async Task<ActionResult<List<Story>>> CreateDraftStories([FromBody] List<Story> stories)
        {
            var created = new List<Story>();
            foreach (var story in stories)
            {
                created.Add(await _storyService.CreateDraftStoryAsync(story));
            }
            return Ok(created);
        }

        [HttpPut]
        public async Task<ActionResult<Story>> UpdateStory([FromBody] Story story)
        {
            var updated = await _storyService.UpdateStoryAsync(story);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteStory(int id)
        {
            var success = await _storyService.DeleteStoryAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}