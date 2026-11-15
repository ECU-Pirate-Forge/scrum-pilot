namespace ScrumPilot.Data.Repositories;

public sealed class OrganizationInvitationConcurrencyException(string message, Exception innerException) : Exception(message, innerException);

public static class OrganizationInvitationRepositoryExceptionClassifier
{
    public static bool IsTransactionConcurrency(Exception exception) =>
        TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(exception);
    public static bool IsMembershipUniqueViolation(Exception exception) =>
        TransactionConcurrencyExceptionClassifier.IsUniqueViolation(exception, "PK_OrganizationMemberships");
}
