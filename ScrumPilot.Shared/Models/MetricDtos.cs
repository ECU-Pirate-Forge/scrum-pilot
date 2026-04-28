namespace ScrumPilot.Shared.Models;

/// <summary>High-level overview of a sprint shown in the dashboard header.</summary>
/// <param name="Name">Display name or goal of the sprint.</param>
/// <param name="StartDate">UTC start date of the sprint.</param>
/// <param name="EndDate">UTC end date of the sprint.</param>
/// <param name="DaysLeft">Calendar days remaining until the sprint ends.</param>
/// <param name="TotalDays">Total calendar days in the sprint.</param>
public record SprintSummaryDto(
    string Name,
    DateTime? StartDate,
    DateTime? EndDate,
    int DaysLeft,
    int TotalDays
);

/// <summary>Story-point and count totals for a sprint's committed vs. completed work.</summary>
/// <param name="CommittedPoints">Total story points committed at sprint start.</param>
/// <param name="CompletedPoints">Story points that have reached <see cref="PbiStatus.Done"/>.</param>
/// <param name="CommittedCount">Number of PBIs committed to the sprint.</param>
/// <param name="CompletedCount">Number of PBIs that have reached Done.</param>
public record SprintProgressDto(
    double CommittedPoints,
    double CompletedPoints,
    int CommittedCount,
    int CompletedCount
);

/// <summary>A single data point on the sprint burndown chart.</summary>
/// <param name="Date">The calendar date for this point.</param>
/// <param name="Actual">Remaining story points on this date, or <c>null</c> for future dates.</param>
/// <param name="Ideal">Ideal remaining points if work burned down perfectly linearly.</param>
public record BurndownPoint(
    DateTime Date,
    double? Actual,
    double Ideal
);

/// <summary>Sprint velocity data point used to render the velocity bar chart.</summary>
/// <param name="SprintName">Display name of the sprint.</param>
/// <param name="Committed">Story points committed at sprint start.</param>
/// <param name="Completed">Story points completed by sprint end.</param>
public record VelocityPoint(
    string SprintName,
    double Committed,
    double Completed
);

/// <summary>A row in the Work-In-Progress table widget.</summary>
/// <param name="PbiId">Identifier of the in-progress PBI.</param>
/// <param name="Title">Title of the PBI.</param>
/// <param name="Type">PBI type as a display string.</param>
/// <param name="Priority">Priority level as a display string.</param>
/// <param name="Status">Current status as a display string.</param>
public record WipItem(
    int PbiId,
    string Title,
    string Type,
    string Priority,
    string Status
);

/// <summary>Daily bug creation and resolution counts for the bug-trend chart.</summary>
/// <param name="Date">The calendar date for this point.</param>
/// <param name="Created">Number of bug PBIs created on this date.</param>
/// <param name="Resolved">Number of bug PBIs resolved (moved to Done) on this date.</param>
public record BugTrendPoint(
    DateTime Date,
    int Created,
    int Resolved
);

/// <summary>Average cycle time (days from In Progress to Done) for a given date.</summary>
/// <param name="Date">The date bucket for this measurement.</param>
/// <param name="AverageDays">Mean number of days PBIs spent in the active workflow.</param>
public record CycleTimePoint(
    DateTime Date,
    double AverageDays
);

/// <summary>Story-point totals split by work type for a given status column.</summary>
/// <param name="Status">The PBI status this point represents.</param>
/// <param name="Features">Story points from Story-type PBIs in this status.</param>
/// <param name="Bugs">Story points from Bug-type PBIs in this status.</param>
public record WorkByStatusPoint(
    string Status,
    double Features,
    double Bugs
);

/// <summary>A single bucket in the time-in-stage heat-map.</summary>
/// <param name="Bucket">The time bucket label (e.g., "0-1 days").</param>
/// <param name="Stage">The workflow stage (e.g., "InProgress").</param>
/// <param name="Count">Number of PBIs that spent this duration in this stage.</param>
public record TimeInStagePoint(string Bucket, string Stage, int Count);

/// <summary>Aggregate time-in-stage data used to render the heat-map widget.</summary>
public class TimeInStageData
{
    /// <summary>Ordered list of workflow stage names used as chart columns.</summary>
    public List<string> Stages { get; set; } = [];

    /// <summary>All time-in-stage bucket counts across every stage.</summary>
    public List<TimeInStagePoint> Points { get; set; } = [];
}

/// <summary>Layout and visibility configuration for a single dashboard widget.</summary>
public class DashboardWidgetConfig
{
    /// <summary>Unique identifier matching the widget's component key.</summary>
    public string Id { get; set; } = "";

    /// <summary>Whether the widget is currently shown on the dashboard.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Column position in the dashboard grid.</summary>
    public int X { get; set; }

    /// <summary>Row position in the dashboard grid.</summary>
    public int Y { get; set; }

    /// <summary>Width in grid columns.</summary>
    public int W { get; set; }

    /// <summary>Height in grid rows.</summary>
    public int H { get; set; }
}

/// <summary>
/// Persisted dashboard layout for a user/project combination,
/// containing the ordered list of widget configurations.
/// </summary>
public class DashboardPreferenceDto
{
    /// <summary>Ordered widget configurations that define the dashboard layout.</summary>
    public List<DashboardWidgetConfig> Widgets { get; set; } = [];
}
