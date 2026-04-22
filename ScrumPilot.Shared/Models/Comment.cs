namespace ScrumPilot.Shared.Models
{
    public class Comment
    {
        public int CommentId { get; set; }
        public int PbiId { get; set; }
        public required string UserId { get; set; }
        public required string Body { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
