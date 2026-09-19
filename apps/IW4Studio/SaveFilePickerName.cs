namespace IW4.Studio.Desktop;

/// <summary>
/// Prepares a suggested save name for a picker that also supplies a default
/// extension. The picker backend applies that extension itself.
/// </summary>
internal static class SaveFilePickerName
{
    public static string WithoutDefaultExtension(
        string suggestedFileName,
        string defaultExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultExtension);

        string suffix = defaultExtension[0] == '.'
            ? defaultExtension
            : $".{defaultExtension}";
        string stem = suggestedFileName;
        while (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
               stem.Length > suffix.Length)
        {
            stem = stem[..^suffix.Length];
        }

        return stem;
    }
}
