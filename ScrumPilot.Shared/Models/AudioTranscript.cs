namespace ScrumPilot.Shared.Models
{
    public class AudioTranscript
    {
        public int Id { get; set; }
        public string Transcript { get; set; } = "";
        public string? Summary { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
