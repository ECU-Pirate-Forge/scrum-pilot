using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public class AtomicDeleteRepositoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_RollsBackPbiUnassignment_WhenEntityDeleteFails(bool deleteSprint)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new DeleteFailureInterceptor();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ScrumPilotContext(options);
        await context.Database.EnsureCreatedAsync();

        var organization = new Organization
        {
            Name = "Test",
            NormalizedName = "TEST",
            CreatedAt = DateTime.UtcNow,
            RowVersion = [1]
        };
        var project = new Project { ProjectName = "Project", Organization = organization };
        var sprint = new Sprint { ProjectId = project.ProjectId, SprintTitle = "Sprint" };
        var epic = new Epic { ProjectId = project.ProjectId, Name = "Epic", DateCreated = DateTime.UtcNow };
        project.Sprints = [sprint];
        project.Epics = [epic];
        var pbi = new ProductBacklogItem
        {
            ProjectId = project.ProjectId,
            Title = "PBI",
            SprintId = null,
            EpicId = null,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
        project.ProductBacklogItems = [pbi];
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        pbi.SprintId = sprint.SprintId;
        pbi.EpicId = epic.EpicId;
        await context.SaveChangesAsync();

        interceptor.TableToFail = deleteSprint ? "Sprint" : "Epic";

        DbUpdateException exception;
        if (deleteSprint)
            exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => new SprintRepository(context).DeleteAsync(sprint.SprintId));
        else
            exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => new EpicRepository(context).DeleteAsync(epic.EpicId));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        context.ChangeTracker.Clear();
        var persistedPbi = await context.Stories.SingleAsync(x => x.PbiId == pbi.PbiId);
        Assert.Equal(sprint.SprintId, persistedPbi.SprintId);
        Assert.Equal(epic.EpicId, persistedPbi.EpicId);
        Assert.True(deleteSprint
            ? await context.Sprints.AnyAsync(x => x.SprintId == sprint.SprintId)
            : await context.Epics.AnyAsync(x => x.EpicId == epic.EpicId));
    }

    private sealed class DeleteFailureInterceptor : DbCommandInterceptor
    {
        public string? TableToFail { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (TableToFail is not null
                && command.CommandText.Contains($"DELETE FROM \"{TableToFail}\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Controlled delete failure.");

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowForConfiguredDelete(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowForConfiguredDelete(DbCommand command)
        {
            if (TableToFail is not null
                && command.CommandText.Contains($"DELETE FROM \"{TableToFail}\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Controlled delete failure.");
        }
    }
}
