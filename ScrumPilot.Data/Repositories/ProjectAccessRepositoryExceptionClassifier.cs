namespace ScrumPilot.Data.Repositories;

public sealed class ProjectAccessConcurrencyException(string message, Exception innerException) : Exception(message, innerException);

public static class ProjectAccessRepositoryExceptionClassifier
{
    public static bool IsTransactionConcurrency(Exception exception) =>
        TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(exception);
    public static bool IsProjectMembershipUniqueViolation(Exception exception) =>
        TransactionConcurrencyExceptionClassifier.IsUniqueViolation(exception, "PK_ProjectMemberships");
}
