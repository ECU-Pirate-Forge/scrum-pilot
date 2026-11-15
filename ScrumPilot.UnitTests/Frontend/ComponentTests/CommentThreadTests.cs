using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests;

public sealed class CommentThreadTests : FrontendTestBase
{
    [Fact]
    public void HistoricalAuthorNamesAreSeparateFromScalarOwnership()
    {
        Authorization.SetAuthorized("visible-name");
        Authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "scalar-1"));
        HttpResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new Comment { CommentId = 1, PbiId = 1, UserId = "scalar-1", AuthorUserName = "Historical author", Body = "Own comment" },
                new Comment { CommentId = 2, PbiId = 1, UserId = "visible-name", AuthorUserName = "Someone else", Body = "Not owned" }
            })
        };
        var cut = Render<CommentThread>(parameters => parameters.Add(component => component.PbiId, 1));
        var comments = cut.FindAll(".comment-item");
        Assert.Contains("Historical author", comments[0].TextContent);
        Assert.Equal(2, comments[0].QuerySelectorAll("button").Length);
        Assert.Empty(comments[1].QuerySelectorAll("button"));
        Assert.Contains("api/organizations/1/comments/pbi/1", HttpRequests);
    }
}
