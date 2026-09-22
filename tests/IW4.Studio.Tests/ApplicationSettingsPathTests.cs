using IW4.Studio.Desktop.Persistence;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class ApplicationSettingsPathTests
{
    [Fact]
    public void Windows_uses_roaming_application_data()
    {
        string applicationData = CreateAbsolutePath("windows", "roaming");

        string? path = AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.Windows,
            applicationData,
            CreateAbsolutePath("unused-profile"),
            CreateAbsolutePath("unused-xdg"));

        Assert.Equal(
            Path.Combine(applicationData, "IW4Studio", "appsettings.json"),
            path);
    }

    [Fact]
    public void MacOS_uses_application_support()
    {
        string userProfile = CreateAbsolutePath("macos", "profile");

        string? path = AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.MacOS,
            CreateAbsolutePath("unused-appdata"),
            userProfile,
            CreateAbsolutePath("unused-xdg"));

        Assert.Equal(
            Path.Combine(
                userProfile,
                "Library",
                "Application Support",
                "IW4Studio",
                "appsettings.json"),
            path);
    }

    [Fact]
    public void Linux_uses_absolute_xdg_config_home()
    {
        string xdgConfigHome = CreateAbsolutePath("linux", "xdg");

        string? path = AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.Linux,
            CreateAbsolutePath("unused-appdata"),
            CreateAbsolutePath("linux", "profile"),
            xdgConfigHome);

        Assert.Equal(
            Path.Combine(xdgConfigHome, "IW4Studio", "appsettings.json"),
            path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/config")]
    public void Linux_ignores_invalid_xdg_config_home(string? xdgConfigHome)
    {
        string userProfile = CreateAbsolutePath("linux", "profile");

        string? path = AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.Linux,
            CreateAbsolutePath("unused-appdata"),
            userProfile,
            xdgConfigHome);

        Assert.Equal(
            Path.Combine(
                userProfile,
                ".config",
                "IW4Studio",
                "appsettings.json"),
            path);
    }

    [Fact]
    public void Missing_platform_root_disables_persistence()
    {
        Assert.Null(AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.Windows,
            string.Empty,
            CreateAbsolutePath("unused-profile"),
            null));
        Assert.Null(AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.MacOS,
            CreateAbsolutePath("unused-appdata"),
            string.Empty,
            null));
        Assert.Null(AppSettingsPath.GetFilePath(
            AppSettingsPath.Platform.Linux,
            CreateAbsolutePath("unused-appdata"),
            string.Empty,
            "relative/config"));
    }

    private static string CreateAbsolutePath(params string[] components) =>
        Path.Combine([Path.GetTempPath(), "IW4.Studio.Tests.Paths", .. components]);
}
