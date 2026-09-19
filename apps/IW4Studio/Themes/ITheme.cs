using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// Describes an application theme independently of any particular control style.
/// </summary>
public interface ITheme
{
    /// <summary>
    /// Gets the user-facing theme name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the stable value used to select and persist the theme.
    /// </summary>
    ThemeMode Mode { get; }

    /// <summary>
    /// Gets the Avalonia theme variant on which the application theme is based.
    /// </summary>
    ThemeVariant BaseVariant { get; }

    /// <summary>
    /// Gets the default application font family.
    /// </summary>
    FontFamily FontFamily { get; }

    /// <summary>
    /// Gets the application banner resource used by the theme.
    /// </summary>
    Uri BannerResource { get; }

    /// <summary>
    /// Gets the semantic colors used to style the application.
    /// </summary>
    ThemePalette Palette { get; }
}
