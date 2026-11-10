using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Components.ScrumBoard;

public sealed record BoardLaneDefinition(PbiStatus Status, string Label, Icon Icon, string CssClass);
