namespace ScrumPilot.Shared.Models
{
    public class MessageTranscript
    {
        public int Id { get; set; }
        public List<DiscordMessage> Messages { get; set; } = new();
    }

    public class DiscordMessage
    {
        public DiscordAuthor Author { get; set; } = new();
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class DiscordAuthor
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
    }
}
