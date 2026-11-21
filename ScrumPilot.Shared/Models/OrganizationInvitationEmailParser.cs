namespace ScrumPilot.Shared.Models;

public static class OrganizationInvitationEmailParser
{
    public static IReadOnlyList<string> Parse(string? suppliedEmails)
    {
        if (string.IsNullOrWhiteSpace(suppliedEmails))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails = new List<string>();
        foreach (var candidate in suppliedEmails.Split(
                     [',', ';', '\r', '\n', '\t', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(candidate))
            {
                emails.Add(candidate);
            }
        }
        return emails;
    }
}
