namespace IW4.Studio.Desktop.Persistence;

/// <summary>
/// Resolves the platform cache location for driver-qualified OpenGL program
/// binaries. The renderer treats a missing location as persistence disabled.
/// </summary>
internal static class OpenGlProgramBinaryCachePath
{
    internal static string? GetDirectory()
    {
        string? cacheRoot;
        if (OperatingSystem.IsWindows())
        {
            cacheRoot = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            cacheRoot = string.IsNullOrWhiteSpace(userProfile)
                ? null
                : Path.Combine(userProfile, "Library", "Caches");
        }
        else
        {
            string? xdgCacheHome =
                Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCacheHome) &&
                Path.IsPathRooted(xdgCacheHome))
            {
                cacheRoot = xdgCacheHome;
            }
            else
            {
                string userProfile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                cacheRoot = string.IsNullOrWhiteSpace(userProfile)
                    ? null
                    : Path.Combine(userProfile, ".cache");
            }
        }

        return string.IsNullOrWhiteSpace(cacheRoot)
            ? null
            : Path.Combine(
                cacheRoot,
                "IW4Studio",
                "OpenGL",
                "ProgramBinaries");
    }
}
