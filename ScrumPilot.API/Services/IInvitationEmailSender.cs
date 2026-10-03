namespace ScrumPilot.API.Services;

public interface IInvitationEmailSender
{
    Task SendAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);
}

public sealed class InvitationDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
