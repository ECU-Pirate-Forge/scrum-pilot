using Microsoft.AspNetCore.SignalR;

namespace ScrumPilot.Web.Pages;

public enum PlanningPokerInvocationFailure
{
    AccessDenied,
    Disconnected,
    Rejected
}

public static class PlanningPokerInvocationError
{
    public static PlanningPokerInvocationFailure Classify(Exception exception)
    {
        if (exception is HubException { Message: "Project not found." })
            return PlanningPokerInvocationFailure.AccessDenied;

        return exception switch
        {
            HubException => PlanningPokerInvocationFailure.Rejected,
            InvalidOperationException or
            ObjectDisposedException or
            OperationCanceledException or
            HttpRequestException or
            IOException or
            System.Net.WebSockets.WebSocketException or
            TimeoutException => PlanningPokerInvocationFailure.Disconnected,
            _ => throw new ArgumentOutOfRangeException(nameof(exception), exception, null)
        };
    }
}
