namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// A collection of Discord messages imported for AI-powered story generation.
    /// </summary>
    public class MessageTranscript
    {
        /// <summary>Unique identifier for this transcript record.</summary>
        public int Id { get; set; }

        /// <summary>The ordered list of messages in this transcript.</summary>
        public List<DiscordMessage> Messages { get; set; } = new();
    }

    /// <summary>
    /// A single message extracted from a Discord channel.
    /// </summary>
    public class DiscordMessage
    {
        /// <summary>The Discord user who sent this message.</summary>
        public DiscordAuthor Author { get; set; } = new();

        /// <summary>Text content of the message.</summary>
        public string Content { get; set; } = "";

        /// <summary>UTC timestamp when the message was sent.</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Identifies the author of a Discord message.
    /// </summary>
    public class DiscordAuthor
    {
        /// <summary>Discord's unique snowflake ID for this user.</summary>
        public string Id { get; set; } = "";

        /// <summary>The user's Discord username.</summary>
        public string Username { get; set; } = "";
    }
}
