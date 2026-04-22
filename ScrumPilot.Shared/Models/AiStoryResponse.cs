using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Strongly-typed representation of the JSON object returned by the AI provider
    /// when generating or improving a Scrum user story.
    /// </summary>
    public class AiStoryResponse
    {
        /// <summary>Short, descriptive title for the generated story.</summary>
        [JsonPropertyName("title")]
        public required string Title { get; set; }

        /// <summary>
        /// Full user story in the format
        /// "As a [role], I want [goal], so that [benefit]."
        /// </summary>
        [JsonPropertyName("userStory")]
        public required string UserStory { get; set; }

        /// <summary>Observable, "I see" acceptance criteria generated for the story.</summary>
        [JsonPropertyName("acceptanceCriteria")]
        public required List<string> AcceptanceCriteria { get; set; }
    }
}