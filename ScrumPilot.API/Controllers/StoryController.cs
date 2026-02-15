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

        [HttpGet]
        public ActionResult<List<Story>> GetStories()
        {
            var stories = _storyService.GetStories();
            return Ok(stories);
        }

        [HttpPost("generate")]
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
    }
}