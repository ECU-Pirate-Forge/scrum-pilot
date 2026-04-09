namespace ScrumPilot.Web.Services;

public class NavMenuStateService
{
    public bool IsExpanded { get; private set; } = true;

    public void Toggle()
    {
        IsExpanded = !IsExpanded;
    }

    public void Expand()
    {
        IsExpanded = true;
    }

    public void Collapse()
    {
        IsExpanded = false;
    }
}