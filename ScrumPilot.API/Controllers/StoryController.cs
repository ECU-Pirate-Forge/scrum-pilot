using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
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
        public async Task<ActionResult<Story>> GenerateAiStory([FromBody] string problemStatement)
        {
            if (string.IsNullOrWhiteSpace(problemStatement))
            {
                return BadRequest("Problem statement is required.");
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

        [HttpPost]
        public async Task<ActionResult<Story>> CreateStory([FromBody] Story story)
        {
            var created = await _storyService.CreateStoryAsync(story);
            return Ok(created);
        }

        [HttpPut]
        public async Task<ActionResult<Story>> UpdateStory([FromBody] Story story)
        {
            var updated = await _storyService.UpdateStoryAsync(story);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteStory(Guid id)
        {
            var success = await _storyService.DeleteStoryAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}