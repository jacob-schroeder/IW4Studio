using IW4.AI.XModel;

namespace Iw4Radiant.Views;

internal static class XModelAdviceText
{
    internal static string Format(XModelImportReport report)
    {
        var lines = new List<string>();
        if (report.UsageDescription is { } usage) lines.Add(usage);
        lines.AddRange(report.Warnings);
        if (report.LodTransitions.Count > 0)
        {
            lines.Add("LOD transitions");
            lines.AddRange(report.LodTransitions);
        }
        if (report.Warnings.Count == 0)
            lines.Add("No advisor warnings for the available native model measurements.");
        return string.Join("\n\n", lines);
    }
}
