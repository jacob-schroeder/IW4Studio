using Avalonia.Media;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// A control-agnostic set of semantic application colors.
/// </summary>
/// <remarks>
/// Controls should consume these roles through Avalonia resources instead of
/// referring directly to literal colors. This keeps interaction states and
/// contrast decisions consistent when the active theme changes.
/// </remarks>
public sealed record ThemePalette(
    ThemeAccentPalette Accent,
    ThemeSurfacePalette Surfaces,
    ThemeTextPalette Text,
    ThemeControlPalette Controls,
    ThemeBorderPalette Borders,
    ThemeStatusPalette Status);

/// <summary>
/// Brand and interaction colors. <see cref="Foreground"/> is used when the
/// primary accent itself is text; <see cref="OnAccent"/> is used for content
/// rendered on a solid primary accent background. The secondary family gives
/// metadata and supporting actions a quieter green treatment.
/// </summary>
public sealed record ThemeAccentPalette(
    Color Default,
    Color Hovered,
    Color Pressed,
    Color Subtle,
    Color Foreground,
    Color OnAccent,
    Color Secondary,
    Color SecondarySubtle,
    Color SecondaryForeground,
    Color OnSecondary);

/// <summary>
/// Background elevation levels, ordered by their semantic role rather than a
/// fixed light-to-dark relationship.
/// </summary>
public sealed record ThemeSurfacePalette(
    Color Canvas,
    Color Default,
    Color Raised,
    Color Sunken,
    Color Overlay,
    Color Sidebar,
    Color Scrim);

/// <summary>
/// Foreground colors for text, icons, and other monochrome content.
/// </summary>
public sealed record ThemeTextPalette(
    Color Primary,
    Color Secondary,
    Color Tertiary,
    Color Disabled);

/// <summary>
/// Background colors for common control and selection states.
/// </summary>
public sealed record ThemeControlPalette(
    Color Default,
    Color Hovered,
    Color Pressed,
    Color Selected,
    Color Disabled,
    Color Input,
    Color Selection,
    Color SelectionForeground);

/// <summary>
/// Border colors for standard, emphasized, disabled, and keyboard-focus states.
/// </summary>
public sealed record ThemeBorderPalette(
    Color Default,
    Color Strong,
    Color Disabled,
    Color Focus);

/// <summary>
/// Semantic feedback colors. These are deliberately distinct from the brand
/// accent so success, warning, and error meaning is never conveyed by hue alone.
/// </summary>
public sealed record ThemeStatusPalette(
    ThemeStatusColor Info,
    ThemeStatusColor Success,
    ThemeStatusColor Warning,
    ThemeStatusColor Danger);

/// <summary>
/// Colors for content, a quiet surface, and its boundary for one status role.
/// </summary>
public sealed record ThemeStatusColor(
    Color Foreground,
    Color Surface,
    Color Border);
