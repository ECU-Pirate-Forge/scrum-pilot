namespace ScrumPilot.Shared.Models;

public record SprintSummaryDto(
    string Name,
    DateTime? StartDate,
    DateTime? EndDate,
    int DaysLeft,
    int TotalDays
);

public record SprintProgressDto(
    double CommittedPoints,
    double CompletedPoints,
    int CommittedCount,
    int CompletedCount
);

public record BurndownPoint(
    DateTime Date,
    double? Actual,
    double Ideal
);

public record VelocityPoint(
    string SprintName,
    double Committed,
    double Completed
);

public record WipItem(
    int PbiId,
    string Title,
    string Type,
    string Priority,
    string Status
);

public record BugTrendPoint(
    DateTime Date,
    int Created,
    int Resolved
);

public record CycleTimePoint(
    DateTime Date,
    double AverageDays
);

public record WorkByStatusPoint(
    string Status,
    double Features,
    double Bugs
);

public record TimeInStagePoint(string Bucket, string Stage, int Count);

public class TimeInStageData
{
    public List<string> Stages { get; set; } = [];
    public List<TimeInStagePoint> Points { get; set; } = [];
}

public class DashboardWidgetConfig
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public class DashboardPreferenceDto
{
    public List<DashboardWidgetConfig> Widgets { get; set; } = [];
}
