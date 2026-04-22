using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class CommentControllerTests
    {
        private readonly ICommentRepository _mockRepo;
        private readonly CommentController _controller;

        public CommentControllerTests()
        {
            _mockRepo = Substitute.For<ICommentRepository>();
            _controller = new CommentController(_mockRepo);
        }

        private static Comment MakeComment(int id = 1, int pbiId = 10, string body = "Test comment") =>
            new() { CommentId = id, PbiId = pbiId, UserId = "user1", Body = body, CreatedDate = DateTime.UtcNow };

        // ── GET api/comments/pbi/{pbiId} ────────────────────────────────────────

        [Fact]
        public async Task GetCommentsByPbiId_ReturnsOk_WithCommentList()
        {
            var comments = new List<Comment> { MakeComment(1, 10), MakeComment(2, 10, "Second") };
            _mockRepo.GetByPbiIdAsync(10).Returns(comments);

            var result = await _controller.GetCommentsByPbiId(10);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var returned = Assert.IsAssignableFrom<IEnumerable<Comment>>(ok.Value);
            Assert.Equal(2, returned.Count());
        }

        [Fact]
        public async Task GetCommentsByPbiId_ReturnsOk_WithEmptyList_WhenNoneExist()
        {
            _mockRepo.GetByPbiIdAsync(99).Returns(new List<Comment>());

            var result = await _controller.GetCommentsByPbiId(99);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Comment>>(ok.Value));
        }

        // ── POST api/comments ───────────────────────────────────────────────────

        [Fact]
        public async Task AddComment_ReturnsCreated_WithNewComment()
        {
            var input = new Comment { PbiId = 10, UserId = "user1", Body = "🚩 Flagged: blocking dependency" };
            var created = MakeComment(5, 10, input.Body);
            _mockRepo.AddAsync(Arg.Any<Comment>()).Returns(created);

            var result = await _controller.AddComment(input);

            var createdAt = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(201, createdAt.StatusCode);
            var returned = Assert.IsType<Comment>(createdAt.Value);
            Assert.Equal(5, returned.CommentId);
            Assert.Equal("🚩 Flagged: blocking dependency", returned.Body);
        }

        [Fact]
        public async Task AddComment_SetsCreatedDateAutomatically()
        {
            var before = DateTime.UtcNow.AddSeconds(-1);
            var input = new Comment { PbiId = 10, UserId = "user1", Body = "any" };
            _mockRepo.AddAsync(Arg.Any<Comment>()).Returns(c => c.Arg<Comment>());

            await _controller.AddComment(input);

            await _mockRepo.Received(1).AddAsync(Arg.Is<Comment>(c => c.CreatedDate >= before));
        }

        [Fact]
        public async Task AddComment_FlagComment_ReturnsCreated()
        {
            var flagComment = new Comment { PbiId = 7, UserId = "alice", Body = "🚩 Flagged: needs clarification" };
            var saved = MakeComment(3, 7, flagComment.Body);
            _mockRepo.AddAsync(Arg.Any<Comment>()).Returns(saved);

            var result = await _controller.AddComment(flagComment);

            Assert.IsType<CreatedAtActionResult>(result.Result);
        }

        [Fact]
        public async Task AddComment_UnflagComment_ReturnsCreated()
        {
            var unflagComment = new Comment { PbiId = 7, UserId = "alice", Body = "🏳️ Flag removed: resolved" };
            var saved = MakeComment(4, 7, unflagComment.Body);
            _mockRepo.AddAsync(Arg.Any<Comment>()).Returns(saved);

            var result = await _controller.AddComment(unflagComment);

            Assert.IsType<CreatedAtActionResult>(result.Result);
        }

        // ── PUT api/comments/{commentId} ────────────────────────────────────────

        [Fact]
        public async Task EditComment_ReturnsOk_WithUpdatedComment()
        {
            var existing = MakeComment(1, 10);
            var updated = MakeComment(1, 10, "Updated body");
            _mockRepo.GetByPbiIdAsync(10).Returns(new List<Comment> { existing });
            _mockRepo.UpdateAsync(Arg.Any<Comment>()).Returns(updated);

            var result = await _controller.EditComment(1, updated);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var returned = Assert.IsType<Comment>(ok.Value);
            Assert.Equal("Updated body", returned.Body);
        }

        [Fact]
        public async Task EditComment_ReturnsBadRequest_WhenIdMismatch()
        {
            var comment = MakeComment(2, 10);

            var result = await _controller.EditComment(99, comment);

            Assert.IsType<BadRequestResult>(result.Result);
        }

        [Fact]
        public async Task EditComment_ReturnsNotFound_WhenCommentDoesNotExist()
        {
            var comment = MakeComment(1, 10);
            _mockRepo.GetByPbiIdAsync(10).Returns(new List<Comment>()); // no match

            var result = await _controller.EditComment(1, comment);

            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ── DELETE api/comments/{commentId} ─────────────────────────────────────

        [Fact]
        public async Task DeleteComment_ReturnsNoContent_WhenSuccessful()
        {
            _mockRepo.DeleteAsync(1).Returns(true);

            var result = await _controller.DeleteComment(1);

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task DeleteComment_ReturnsNotFound_WhenCommentDoesNotExist()
        {
            _mockRepo.DeleteAsync(99).Returns(false);

            var result = await _controller.DeleteComment(99);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
