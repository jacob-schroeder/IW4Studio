using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using IW4.FastFiles.Database;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileDetails;

/// <summary>
/// Read-only projection of the target fastfile's preserved container header.
/// </summary>
public sealed class FastFileDetailsToolViewModel
{
    public FastFileDetailsToolViewModel(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        DbHeader header = workspace.TargetSource.ContainerEnvelope;
        FileName = Path.GetFileName(workspace.TargetSource.PhysicalPath);
        Magic = header.Magic;
        MagicType = header.MagicType;
        Version = GetDisplayName(header.Version);
        OnlineUpdatable = header.AllowOnlineUpdate ? "Yes" : "No";
        FileCreationTime = header.FileCreationTime.ToString(
            "MM/dd/yyyy hh:mm tt",
            CultureInfo.InvariantCulture);
        Language = GetLanguageDisplayName(header.LanguageMask);
        StreamedImageCount = header.EntryCount.ToString("N0", CultureInfo.InvariantCulture);
    }

    public string FileName { get; }

    public string Magic { get; }

    public string MagicType { get; }

    public string Version { get; }

    public string OnlineUpdatable { get; }

    public string FileCreationTime { get; }

    public string Language { get; }

    public string StreamedImageCount { get; }

    private static string GetLanguageDisplayName(uint languageMask)
    {
        string[] displayNames = Enumerable.Range(0, 15)
            .Where(bitIndex => (languageMask & (1u << bitIndex)) != 0)
            .Select(bitIndex => GetDisplayName((XFileLanguage)(bitIndex + 1)))
            .ToArray();

        return displayNames.Length > 0
            ? string.Join(", ", displayNames)
            : $"0x{languageMask:X8}";
    }

    private static string GetDisplayName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        FieldInfo? field = typeof(TEnum).GetField(value.ToString());
        return field?.GetCustomAttribute<DisplayAttribute>()?.GetName()
            ?? value.ToString();
    }
}
