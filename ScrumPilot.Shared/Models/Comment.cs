namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents a comment left by a team member on a PBI.
    /// </summary>
    public class Comment
    {
        /// <summary>Unique identifier for this comment.</summary>
        public int CommentId { get; set; }

        /// <summary>The PBI this comment is attached to.</summary>
        public int PbiId { get; set; }

        /// <summary>Identity ID of the user who authored this comment.</summary>
        public required string UserId { get; set; }

        /// <summary>Text content of the comment.</summary>
        public required string Body { get; set; }

        /// <summary>UTC timestamp when this comment was created.</summary>
        public DateTime CreatedDate { get; set; }
    }
}
