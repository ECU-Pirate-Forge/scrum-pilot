namespace ScrumPilot.FluentWeb.Components.Pbi;

internal static class PbiFormatting
{
    public static string Label(Enum value) => value.ToString() switch
    {
        "ToDo" => "To Do",
        "InProgress" => "In Progress",
        "InReview" => "In Review",
        "TwentyOne" => "21",
        _ => value.ToString()
    };
}
