using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models
{
    public class AiStoryResponse
    {
        [JsonPropertyName("title")]
        public required string Title { get; set; }

        [JsonPropertyName("userStory")]
        public required string UserStory { get; set; }

        [JsonPropertyName("acceptanceCriteria")]
        public required List<string> AcceptanceCriteria { get; set; }
    }
}