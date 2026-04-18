using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class MetricsDashboardService : IMetricsDashboardService
{
    private readonly ISprintRepository _sprints;
    private readonly IPbiRepository _pbis;
    private readonly IPbiHistoryRepository _history;

    public MetricsDashboardService(
        ISprintRepository sprints,
        IPbiRepository pbis,
        IPbiHistoryRepository history)
    {
        _sprints = sprints;
        _pbis = pbis;
        _history = history;
    }

    public async Task<SprintSummaryDto?> GetSprintSummaryAsync(int sprintId)
    {
        var allSprints = await _sprints.GetAllSprintsAsync();
        var sprint = allSprints.FirstOrDefault(s => s.SprintId == sprintId);
        if (sprint is null) return null;

        var now = DateTime.UtcNow.Date;
        var end = sprint.EndDate?.Date ?? now;
        var start = sprint.StartDate?.Date ?? now;
        var totalDays = Math.Max(1, (end - start).Days);
        var daysLeft = Math.Max(0, (end - now).Days);

        return new SprintSummaryDto(
            sprint.SprintGoal ?? $"Sprint {sprint.SprintId}",
            sprint.StartDate,
            sprint.EndDate,
            daysLeft,
            totalDays);
    }

    public async Task<SprintProgressDto> GetSprintProgressAsync(int sprintId)
    {
        var items = (await _pbis.GetFilteredPbisAsync(sprintId, null)).ToList();
        var committed = items.Sum(p => (int)p.StoryPoints);
        var completed = items.Where(p => p.Status == PbiStatus.Done).Sum(p => (int)p.StoryPoints);
        return new SprintProgressDto(committed, completed, items.Count, items.Count(p => p.Status == PbiStatus.Done));
    }

    public async Task<List<BurndownPoint>> GetBurndownDataAsync(int sprintId)
    {
        var allSprints = await _sprints.GetAllSprintsAsync();
        var sprint = allSprints.FirstOrDefault(s => s.SprintId == sprintId);
        if (sprint?.StartDate is null || sprint.EndDate is null) return [];

        var items = (await _pbis.GetFilteredPbisAsync(sprintId, null)).ToList();
        var history = (await _history.GetHistoryForSprintAsync(sprintId)).ToList();

        var start = sprint.StartDate.Value.Date;
        var end   = sprint.EndDate.Value.Date;
        var today = DateTime.UtcNow.Date;
        var totalPoints = items.Sum(p => (int)p.StoryPoints);

        // First/last business day — used to clamp completions that fall outside the window.
        var firstBizDay = start;
        while (firstBizDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            firstBizDay = firstBizDay.AddDays(1);

        var lastBizDay = end;
        while (lastBizDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            lastBizDay = lastBizDay.AddDays(-1);

        if (firstBizDay > lastBizDay) return [];

        // Count business days so the ideal slope is correct.
        int totalBizDays = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                totalBizDays++;

        // Snap a completion date to the nearest preceding business day inside [start, end].
        // This ensures every Done item is counted even if marked done on a weekend or
        // after the sprint officially ended.
        DateTime SnapToBizDay(DateTime date)
        {
            var d = date > end ? lastBizDay : date < start ? firstBizDay : date;
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                d = d.AddDays(-1);
            return d < firstBizDay ? firstBizDay : d;
        }

        // Build map: business date -> points completed that day.
        var completionsByDate = new Dictionary<DateTime, double>();
        foreach (var item in items)
        {
            var pts = (int)item.StoryPoints;
            if (pts == 0) continue;

            var doneEntry = history
                .Where(h => h.PbiId == item.PbiId && h.ToStatus == PbiStatus.Done)
                .OrderBy(h => h.ChangedAt)
                .FirstOrDefault();

            DateTime? doneDate = doneEntry?.ChangedAt.Date;
            if (doneDate is null && item.Status == PbiStatus.Done)
                doneDate = item.LastUpdated.Date;

            if (!doneDate.HasValue) continue;

            var effectiveDate = SnapToBizDay(doneDate.Value);
            completionsByDate[effectiveDate] = completionsByDate.GetValueOrDefault(effectiveDate) + pts;
        }

        // Iterate every calendar day. The ideal line only decrements on business days
        // and stays flat on weekends — exactly like Jira. The actual line also shows
        // on weekends (flat, since completions are snapped to weekdays above).
        var denominator = Math.Max(1, totalBizDays - 1);
        var points = new List<BurndownPoint>();
        double remaining = totalPoints;
        double currentIdeal = totalPoints;
        int bizDaysSeen = 0;

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            bool isBizDay = day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

            if (isBizDay)
            {
                // Advance the ideal before recording so day 0 = totalPoints, last day = 0.
                currentIdeal = totalPoints * (1.0 - (double)bizDaysSeen / denominator);
                bizDaysSeen++;
            }
            // On weekends currentIdeal stays at whatever Friday left it.

            if (day <= today)
            {
                remaining -= completionsByDate.GetValueOrDefault(day, 0);
                points.Add(new BurndownPoint(day, Math.Max(0, remaining), Math.Max(0, currentIdeal)));
            }
            else
            {
                points.Add(new BurndownPoint(day, null, Math.Max(0, currentIdeal)));
            }
        }

        return points;
    }

    public async Task<List<VelocityPoint>> GetVelocityDataAsync(int? currentSprintId = null)
    {
        var allSprints = (await _sprints.GetAllSprintsAsync()).ToList();

        // Upper bound: if a sprint is selected use its start date so we show
        // sprints up to and including it (open sprints are included this way).
        DateTime? ceiling = null;
        if (currentSprintId.HasValue)
        {
            var sel = allSprints.FirstOrDefault(s => s.SprintId == currentSprintId.Value);
            ceiling = sel?.StartDate;
        }

        var closedSprints = allSprints
            .Where(s => s.StartDate.HasValue && (ceiling == null || s.StartDate <= ceiling))
            .OrderByDescending(s => s.StartDate)
            .Take(5)
            .Reverse()
            .ToList();

        var result = new List<VelocityPoint>();
        foreach (var sprint in closedSprints)
        {
            var items = (await _pbis.GetFilteredPbisAsync(sprint.SprintId, null)).ToList();
            var committed = (double)items.Sum(p => (int)p.StoryPoints);
            var completed = (double)items.Where(p => p.Status == PbiStatus.Done).Sum(p => (int)p.StoryPoints);
            result.Add(new VelocityPoint(sprint.SprintGoal ?? $"Sprint {sprint.SprintId}", committed, completed));
        }
        return result;
    }

    public async Task<List<WipItem>> GetWipItemsAsync(int sprintId)
    {
        var items = await _pbis.GetFilteredPbisAsync(sprintId, null);
        return items
            .Where(p => p.Status == PbiStatus.InProgress)
            .Select(p => new WipItem(p.PbiId, p.Title, p.Type.ToString(), p.Priority.ToString(), p.Status.ToString()))
            .ToList();
    }

    public async Task<List<BugTrendPoint>> GetBugTrendAsync(int sprintId)
    {
        var allSprints = await _sprints.GetAllSprintsAsync();
        var sprint = allSprints.FirstOrDefault(s => s.SprintId == sprintId);
        if (sprint?.StartDate is null) return [];

        var items = (await _pbis.GetFilteredPbisAsync(sprintId, null)).ToList();
        var bugs = items.Where(p => p.Type == PbiType.Bug).ToList();
        var history = (await _history.GetHistoryForSprintAsync(sprintId)).ToList();

        var start = sprint.StartDate.Value.Date;
        var end = sprint.EndDate?.Date ?? DateTime.UtcNow.Date;
        var lastDay = end < DateTime.UtcNow.Date ? end : DateTime.UtcNow.Date;

        // Map bug PbiId -> first Done date
        var resolvedDates = new Dictionary<int, DateTime>();
        foreach (var bug in bugs)
        {
            var doneEntry = history
                .Where(h => h.PbiId == bug.PbiId && h.ToStatus == PbiStatus.Done)
                .OrderBy(h => h.ChangedAt)
                .FirstOrDefault();
            if (doneEntry is not null)
                resolvedDates[bug.PbiId] = doneEntry.ChangedAt.Date;
            else if (bug.Status == PbiStatus.Done)
                resolvedDates[bug.PbiId] = bug.LastUpdated.Date;
        }

        var points = new List<BugTrendPoint>();
        for (var day = start; day <= lastDay; day = day.AddDays(1))
        {
            var created = bugs.Count(b => b.DateCreated.Date == day);
            var resolved = resolvedDates.Values.Count(d => d == day);
            points.Add(new BugTrendPoint(day, created, resolved));
        }
        return points;
    }

    public async Task<List<CycleTimePoint>> GetCycleTimeDataAsync(int sprintId)
    {
        var items = (await _pbis.GetFilteredPbisAsync(sprintId, null)).ToList();
        var history = (await _history.GetHistoryForSprintAsync(sprintId)).ToList();

        var cycleTimes = new Dictionary<DateTime, List<double>>();

        foreach (var item in items)
        {
            var itemHistory = history.Where(h => h.PbiId == item.PbiId).OrderBy(h => h.ChangedAt).ToList();

            DateTime? inProgressDate = itemHistory
                .FirstOrDefault(h => h.ToStatus == PbiStatus.InProgress)?.ChangedAt;
            DateTime? doneDate = itemHistory
                .LastOrDefault(h => h.ToStatus == PbiStatus.Done)?.ChangedAt;

            if (inProgressDate.HasValue && doneDate.HasValue && doneDate > inProgressDate)
            {
                var completionDay = doneDate.Value.Date;
                var cycleTime = (doneDate.Value - inProgressDate.Value).TotalDays;
                if (!cycleTimes.ContainsKey(completionDay))
                    cycleTimes[completionDay] = [];
                cycleTimes[completionDay].Add(cycleTime);
            }
        }

        return cycleTimes
            .OrderBy(kv => kv.Key)
            .Select(kv => new CycleTimePoint(kv.Key, kv.Value.Average()))
            .ToList();
    }

    public async Task<List<WorkByStatusPoint>> GetWorkByStatusAsync(int sprintId)
    {
        var items = await _pbis.GetFilteredPbisAsync(sprintId, null);
        var byStatus = items.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.ToList());

        var statusOrder = new[] { PbiStatus.ToDo, PbiStatus.InProgress, PbiStatus.InReview, PbiStatus.Done };
        return statusOrder.Select(s =>
        {
            var group = byStatus.TryGetValue(s, out var list) ? list : [];
            return new WorkByStatusPoint(
                s.ToString(),
                group.Count(p => p.Type != PbiType.Bug),
                group.Count(p => p.Type == PbiType.Bug));
        }).ToList();
    }

    private static readonly string[] BucketOrder = ["≤1d", "2-3d", "4-8d", "9+d"];

    private static readonly HashSet<string> ExcludedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(PbiStatus.ToDo), nameof(PbiStatus.Done)
    };

    private static string GetBucket(double days) => (int)Math.Ceiling(days) switch
    {
        <= 1 => "≤1d",
        <= 3 => "2-3d",
        <= 8 => "4-8d",
        _    => "9+d"
    };

    public async Task<TimeInStageData> GetTimeInStageDataAsync(int sprintId)
    {
        var items = (await _pbis.GetFilteredPbisAsync(sprintId, null)).ToList();
        var history = (await _history.GetHistoryForSprintAsync(sprintId)).ToList();

        // stage -> bucket -> count
        var data = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var itemHistory = history.Where(h => h.PbiId == item.PbiId).OrderBy(h => h.ChangedAt).ToList();
            if (itemHistory.Count == 0) continue;

            // Compute time spent in each status by walking transitions
            var accumulated = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            DateTime? enteredCurrentAt = null;
            string? currentStatus = null;

            foreach (var entry in itemHistory)
            {
                if (currentStatus is not null && enteredCurrentAt.HasValue)
                {
                    var spent = (entry.ChangedAt - enteredCurrentAt.Value).TotalDays;
                    accumulated[currentStatus] = accumulated.GetValueOrDefault(currentStatus) + Math.Max(0, spent);
                }
                currentStatus = entry.ToStatus.ToString();
                enteredCurrentAt = entry.ChangedAt;
            }

            foreach (var (status, days) in accumulated)
            {
                if (ExcludedStatuses.Contains(status) || days <= 0) continue;
                var bucket = GetBucket(days);
                if (!data.ContainsKey(status))
                    data[status] = new(StringComparer.OrdinalIgnoreCase);
                data[status][bucket] = data[status].GetValueOrDefault(bucket) + 1;
            }
        }

        if (data.Count == 0) return new();

        var stages = data
            .OrderByDescending(kv => kv.Value.Values.Sum())
            .Select(kv => kv.Key)
            .ToList();

        var points = stages
            .SelectMany(stage => BucketOrder.Select(bucket =>
                new TimeInStagePoint(bucket, stage, data[stage].GetValueOrDefault(bucket))))
            .ToList();

        return new TimeInStageData { Stages = stages, Points = points };
    }
}
