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
        private readonly IStoryRepository _storyRepository;

        public StoryController(IStoryService storyService, IStoryRepository storyRepository)
        {
            _storyService = storyService;
            _storyRepository = storyRepository;
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
                // Save the generated story to the database
                var savedStory = await _storyRepository.AddAsync(story);
                return Ok(savedStory);
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