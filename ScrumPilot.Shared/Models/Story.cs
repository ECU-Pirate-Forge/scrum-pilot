using System;
using System.Collections.Generic;
using System.Text;

namespace ScrumPilot.Shared.Models
{
    public class Story
    {
        public Guid Id { get; set; }

        public required string Title { get; set; }

        public string Description { get; set; } = "";

        public StoryStatus Status { get; set; }
        public StoryPriority Priority { get; set; }

        public int? StoryPoints { get; set; }

        public bool IsAiGenerated { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
