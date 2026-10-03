using Microsoft.AspNetCore.SignalR;
using ScrumPilot.Web.Pages;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public class PlanningPokerInvocationErrorTests
{
    [Fact]
    public void Classify_ProjectNotFoundHubException_AsAccessDenied()
    {
        var result = PlanningPokerInvocationError.Classify(
            new HubException("Project not found."));

        Assert.Equal(PlanningPokerInvocationFailure.AccessDenied, result);
    }

    [Theory]
    [MemberData(nameof(ConnectionExceptions))]
    public void Classify_ConnectionException_AsDisconnected(Exception exception)
    {
        var result = PlanningPokerInvocationError.Classify(exception);

        Assert.Equal(PlanningPokerInvocationFailure.Disconnected, result);
    }

    public static TheoryData<Exception> ConnectionExceptions => new()
    {
        new InvalidOperationException(),
        new ObjectDisposedException("hub"),
        new OperationCanceledException(),
        new HttpRequestException(),
        new TimeoutException(),
        new IOException(),
        new System.Net.WebSockets.WebSocketException()
    };

    [Fact]
    public void Classify_OtherHubException_AsRejected()
    {
        var result = PlanningPokerInvocationError.Classify(
            new HubException("Product backlog item not found."));

        Assert.Equal(PlanningPokerInvocationFailure.Rejected, result);
    }
}
