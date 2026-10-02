using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Strongly-typed representation of an item in the JSON array returned by the AI provider
    /// when generating or improving a Scrum product backlog item.
    /// </summary>
    public class AiStoryResponse
    {
        /// <summary>The generated work-item type.</summary>
        [JsonPropertyName("type")]
        public PbiType? Type { get; set; }

        /// <summary>The generated priority.</summary>
        [JsonPropertyName("priority")]
        public PbiPriority? Priority { get; set; }

        /// <summary>The generated Fibonacci story-point estimate.</summary>
        [JsonPropertyName("storyPoints")]
        public int? StoryPoints { get; set; }

        /// <summary>Short, descriptive title for the generated story.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Full user story in the format
        /// "As a [role], I want [goal], so that [benefit]."
        /// </summary>
        [JsonPropertyName("userStory")]
        public string? UserStory { get; set; }

        /// <summary>Observable, "I see" acceptance criteria generated for the story.</summary>
        [JsonPropertyName("acceptanceCriteria")]
        public List<string>? AcceptanceCriteria { get; set; }
    }
}