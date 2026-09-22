namespace IW4.Studio.Desktop.Persistence;

/// <summary>
/// Resolves IW4 Studio's per-user configuration file without coupling it to
/// the executable or working directory.
/// </summary>
internal static class AppSettingsPath
{
    private const string ApplicationNamespace = "IW4Studio";
    private const string SettingsFileName = "appsettings.json";

    internal static string? GetDefaultFilePath() => GetFilePath(
        GetCurrentPlatform(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    internal static string? GetFilePath(
        Platform platform,
        string applicationData,
        string userProfile,
        string? xdgConfigHome)
    {
        string? configurationRoot = platform switch
        {
            Platform.Windows => applicationData,
            Platform.MacOS => CombineIfFullyQualified(
                userProfile,
                "Library",
                "Application Support"),
            _ when IsFullyQualified(xdgConfigHome) => xdgConfigHome,
            _ => CombineIfFullyQualified(userProfile, ".config")
        };

        return IsFullyQualified(configurationRoot)
            ? Path.Combine(
                configurationRoot!,
                ApplicationNamespace,
                SettingsFileName)
            : null;
    }

    private static Platform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return Platform.Windows;
        if (OperatingSystem.IsMacOS())
            return Platform.MacOS;
        return Platform.Linux;
    }

    private static string? CombineIfFullyQualified(
        string path,
        params string[] components) =>
        IsFullyQualified(path)
            ? Path.Combine([path, .. components])
            : null;

    private static bool IsFullyQualified(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    internal enum Platform
    {
        Windows,
        MacOS,
        Linux
    }
}
