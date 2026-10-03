using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public class TenantIsolationControllerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IOrganizationAccessService _access = Substitute.For<IOrganizationAccessService>();

    public TenantIsolationControllerTests()
    {
        _currentUser.UserId.Returns("user-a");
    }

    [Fact]
    public async Task PbiList_ReturnsNotFound_ForInaccessibleProject()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        var controller = new PbiController(service, repository, _currentUser, _access);
        _access.CanAccessProjectAsync("user-a", 22).Returns(false);

        var result = await controller.GetAllPbis(22, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 22, default);
        await repository.DidNotReceive().GetByProjectAsync(
            Arg.Any<int>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PbiNonDraftList_SprintSentinel_FiltersUnassignedPbisWithoutSprintValidation()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        var expected = new List<ProductBacklogItem>
        {
            new() { PbiId = 7, ProjectId = 11, Title = "Unassigned" }
        };
        _access.CanAccessProjectAsync("user-a", 11, default).Returns(true);
        service.GetFilteredPbisAsync(-1, null, 11, default).Returns(expected);
        var controller = new PbiController(service, repository, _currentUser, _access);

        var result = await controller.GetNonDraftPbis(11, -1, null, default);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(result.Result).Value);
        await service.Received(1).GetFilteredPbisAsync(-1, null, 11, default);
        await _access.DidNotReceive().SprintBelongsToProjectAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PbiNonDraftList_SprintSentinel_ReturnsNotFoundForInaccessibleProject()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        _access.CanAccessProjectAsync("user-a", 22, default).Returns(false);
        var controller = new PbiController(service, repository, _currentUser, _access);

        var result = await controller.GetNonDraftPbis(22, -1, null, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await service.DidNotReceive().GetFilteredPbisAsync(
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PbiUpdate_UsesPersistedProject_AndCannotReparent()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        repository.GetByIdAsync(5, default).Returns(new ProductBacklogItem
        {
            PbiId = 5, ProjectId = 11, Title = "old"
        });
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        service.UpdatePbiAsync(Arg.Any<ProductBacklogItem>(), default)
            .Returns(call => call.Arg<ProductBacklogItem>());
        var controller = new PbiController(service, repository, _currentUser, _access);

        var result = await controller.UpdatePbi(new ProductBacklogItem
        {
            PbiId = 5, ProjectId = 22, Title = "new"
        }, default);

        Assert.Equal(11, Assert.IsType<ProductBacklogItem>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ProjectId);
        await _access.Received(1).CanAccessProjectAsync("user-a", 11, default);
    }

    [Fact]
    public async Task SprintList_ReturnsNotFound_ForInaccessibleProject()
    {
        var service = Substitute.For<ISprintService>();
        var repository = Substitute.For<ISprintRepository>();
        var controller = new SprintController(service, repository, _currentUser, _access);
        _access.CanAccessProjectAsync("user-a", 22).Returns(false);

        var result = await controller.GetAllSprints(22, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 22, default);
        await service.DidNotReceive().GetSprintsByProjectAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommentCreate_DerivesAuthorFromCurrentUser()
    {
        var comments = Substitute.For<ICommentRepository>();
        var pbis = Substitute.For<IPbiRepository>();
        pbis.GetByIdAsync(7, default).Returns(new ProductBacklogItem
        {
            PbiId = 7, ProjectId = 11, Title = "pbi"
        });
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        comments.AddAsync(Arg.Any<Comment>(), default).Returns(call => call.Arg<Comment>());
        var controller = new CommentController(comments, pbis, _currentUser, _access);

        await controller.AddComment(new Comment
        {
            PbiId = 7, UserId = "user-b", Body = "hello"
        }, default);

        await comments.Received().AddAsync(
            Arg.Is<Comment>(comment => comment.UserId == "user-a"), default);
    }

    [Fact]
    public async Task CommentEdit_ReturnsNotFound_WhenCurrentUserIsNotAuthor()
    {
        var comments = Substitute.For<ICommentRepository>();
        var pbis = Substitute.For<IPbiRepository>();
        comments.GetByIdAsync(3, default).Returns(new Comment
        {
            CommentId = 3, PbiId = 7, UserId = "user-b", Body = "old"
        });
        pbis.GetByIdAsync(7, default).Returns(new ProductBacklogItem
        {
            PbiId = 7, ProjectId = 11, Title = "pbi"
        });
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        var controller = new CommentController(comments, pbis, _currentUser, _access);

        var result = await controller.EditComment(3, new Comment
        {
            CommentId = 3, PbiId = 99, UserId = "user-a", Body = "new"
        }, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await comments.DidNotReceive().UpdateAsync(Arg.Any<Comment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DashboardPreference_ReturnsNotFound_ForInaccessibleProject()
    {
        var service = Substitute.For<IDashboardPreferenceService>();
        var controller = new DashboardPreferenceController(service, _currentUser, _access);
        _access.CanAccessProjectAsync("user-a", 22).Returns(false);

        var result = await controller.Get(22, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 22, default);
        await service.DidNotReceive().GetPreferencesAsync(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task PbiCreate_RejectsForeignRelatedId()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        _access.EpicBelongsToProjectAsync(22, 11).Returns(false);
        var controller = new PbiController(service, repository, _currentUser, _access);

        var result = await controller.CreatePbi(11, new ProductBacklogItem
        {
            ProjectId = 11, EpicId = 22, Title = "foreign epic"
        });

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 11, default);
        await _access.Received(1).EpicBelongsToProjectAsync(22, 11, default);
        await service.DidNotReceive().CreatePbiAsync(
            Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PbiBulkCreate_ValidatesAllItemsBeforePersisting()
    {
        var service = Substitute.For<IPbiService>();
        var repository = Substitute.For<IPbiRepository>();
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        _access.EpicBelongsToProjectAsync(1, 11).Returns(true);
        _access.EpicBelongsToProjectAsync(22, 11).Returns(false);
        var controller = new PbiController(service, repository, _currentUser, _access);

        var result = await controller.CreatePbis(11,
        [
            new ProductBacklogItem { EpicId = 1, Title = "valid" },
            new ProductBacklogItem { EpicId = 22, Title = "foreign" }
        ]);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 11, default);
        await _access.Received(1).EpicBelongsToProjectAsync(1, 11, default);
        await _access.Received(1).EpicBelongsToProjectAsync(22, 11, default);
        await service.DidNotReceive().CreatePbisAsync(
            Arg.Any<IEnumerable<ProductBacklogItem>>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SprintUpdate_PreservesPersistedProject()
    {
        var service = Substitute.For<ISprintService>();
        var repository = Substitute.For<ISprintRepository>();
        repository.GetByIdAsync(5, default).Returns(new Sprint
        {
            SprintId = 5, ProjectId = 11, SprintTitle = "old"
        });
        _access.CanAccessProjectAsync("user-a", 11).Returns(true);
        service.UpdateAsync(Arg.Any<Sprint>(), default).Returns(call => call.Arg<Sprint>());
        var controller = new SprintController(service, repository, _currentUser, _access);

        var result = await controller.Update(5, new Sprint
        {
            SprintId = 5, ProjectId = 22, SprintTitle = "new"
        });

        Assert.Equal(11, Assert.IsType<Sprint>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ProjectId);
    }

    [Fact]
    public async Task Metrics_ReturnNotFound_ForForeignSprint()
    {
        var service = Substitute.For<IMetricsDashboardService>();
        var sprints = Substitute.For<ISprintRepository>();
        sprints.GetByIdAsync(22, default).Returns(new Sprint
        {
            SprintId = 22, ProjectId = 22
        });
        _access.CanAccessProjectAsync("user-a", 22).Returns(false);
        var controller = new MetricsDashboardController(service, sprints, _currentUser, _access);

        var result = await controller.GetSprintProgress(22);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 22, default);
        await service.DidNotReceive().GetSprintProgressAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task ProjectUsers_ReturnNotFound_ForForeignProject()
    {
        var service = Substitute.For<IUserSettingsService>();
        _access.CanAccessProjectAsync("user-a", 22).Returns(false);
        var controller = new UserController(service, _currentUser, _access);

        var result = await controller.GetAllUsers(22, default);

        Assert.IsType<NotFoundResult>(result.Result);
        await _access.Received(1).CanAccessProjectAsync("user-a", 22, default);
        await service.DidNotReceive().GetProjectUsersAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserSettings_ReturnsBadRequestForInvalidDefaultProject()
    {
        var service = Substitute.For<IUserSettingsService>();
        service.UpdateSettingsAsync(
                "user-a",
                Arg.Any<UserSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(UserSettingsUpdateResult.Validation(
                "The selected default project is not available."));
        var controller = new UserController(service, _currentUser, _access);

        var result = await controller.UpdateSettings(new UserSettingsDto
        {
            DefaultProjectId = 22
        });

        Assert.IsType<BadRequestObjectResult>(result);
        await service.Received(1).UpdateSettingsAsync(
            "user-a",
            Arg.Any<UserSettingsDto>(),
            default);
    }
}
