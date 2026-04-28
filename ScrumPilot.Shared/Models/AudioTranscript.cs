namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Stores the transcribed text of a recorded audio standup or meeting.
    /// </summary>
    public class AudioTranscript
    {
        /// <summary>Unique identifier for this transcript record.</summary>
        public int Id { get; set; }

        /// <summary>Full transcribed text of the audio recording.</summary>
        public string Transcript { get; set; } = "";

        /// <summary>Optional AI-generated summary of the transcript content.</summary>
        public string? Summary { get; set; }

        /// <summary>UTC timestamp when the audio was recorded.</summary>
        public DateTime RecordedAt { get; set; }
    }
}
