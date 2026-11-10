using ApexCharts;

namespace ScrumPilot.FluentWeb.Components.MetricsDashboard;

public static class MetricsChartTheme
{
    public static Mode GetApexMode(bool isDarkMode) => isDarkMode ? Mode.Dark : Mode.Light;

    public static string AxisText(bool isDarkMode) => isDarkMode ? "#cbd5e1" : "#475569";

    public static string GridLine(bool isDarkMode) => isDarkMode ? "#334155" : "#e2e8f0";

    public static string AnnotationBackground(bool isDarkMode) => isDarkMode ? "#0f172a" : "#ffffff";

    public static Theme Theme(bool isDarkMode) => new() { Mode = GetApexMode(isDarkMode) };

    public static Tooltip Tooltip(bool isDarkMode, bool shared = false) => new()
    {
        Theme = GetApexMode(isDarkMode),
        Shared = shared
    };

    public static AxisLabelStyle AxisLabelStyle(bool isDarkMode) => new()
    {
        Colors = AxisText(isDarkMode),
        FontSize = "11px"
    };

    public static LegendLabels LegendLabels(bool isDarkMode) => new()
    {
        Colors = [AxisText(isDarkMode)]
    };
}
