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
                var story = await _storyService.GenerateAiStory(problemStatement);

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
    }
}