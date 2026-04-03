using System;
using System.Collections.Generic;
using System.Text;

namespace ScrumPilot.Shared.Models
{
    public enum StoryPoints
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Three = 3,
        Five = 5,
        Eight = 8,
        Thirteen = 13,
        TwentyOne = 21
    }

    public class Story
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public string Description { get; set; } = "";
        public StoryStatus Status { get; set; }
        public StoryPriority Priority { get; set; }
        public StoryPoints StoryPoints { get; set; }
        public StoryOrigin Origin { get; set; }
        public bool IsDraft { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}