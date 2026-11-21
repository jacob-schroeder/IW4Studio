using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Themes.Design;

/// <summary>
/// A Rider-inspired light theme customized with the IW4 Studio green accent.
/// </summary>
public sealed class LightTheme : ITheme
{
    public string Name => "IW4 Studio Light";

    public ThemeMode Mode => ThemeMode.Light;

    public ThemeVariant BaseVariant => ThemeVariant.Light;

    public FontFamily FontFamily { get; } = new("Inter");

    public ThemePalette Palette { get; } = new(
        Accent: new ThemeAccentPalette(
            Default: Color.FromUInt32(0xFF8BD21C),
            Hovered: Color.FromUInt32(0xFF7CC218),
            Pressed: Color.FromUInt32(0xFF6EAF14),
            Subtle: Color.FromUInt32(0xFFEFF8E2),
            Foreground: Color.FromUInt32(0xFF4E7A0F),
            OnAccent: Color.FromUInt32(0xFF172006),
            Secondary: Color.FromUInt32(0xFF5F940F),
            SecondarySubtle: Color.FromUInt32(0xFFE6F2D2),
            SecondaryForeground: Color.FromUInt32(0xFF42680C),
            OnSecondary: Color.FromUInt32(0xFF172006)),
        Surfaces: new ThemeSurfacePalette(
            Canvas: Color.FromUInt32(0xFFFFFFFF),
            Default: Color.FromUInt32(0xFFF7F8FA),
            Raised: Color.FromUInt32(0xFFFFFFFF),
            Sunken: Color.FromUInt32(0xFFF2F3F5),
            Overlay: Color.FromUInt32(0xFFFFFFFF),
            Sidebar: Color.FromUInt32(0xFFF2F3F5),
            Scrim: Color.FromUInt32(0x66000000)),
        Text: new ThemeTextPalette(
            Primary: Color.FromUInt32(0xFF1F2329),
            Secondary: Color.FromUInt32(0xFF5F636B),
            Tertiary: Color.FromUInt32(0xFF6D7179),
            Disabled: Color.FromUInt32(0xFFA8ADBD)),
        Controls: new ThemeControlPalette(
            Default: Color.FromUInt32(0xFFFFFFFF),
            Hovered: Color.FromUInt32(0xFFF2F3F5),
            Pressed: Color.FromUInt32(0xFFE7E8EB),
            Selected: Color.FromUInt32(0xFFE8F4D5),
            Disabled: Color.FromUInt32(0xFFF2F3F5),
            Input: Color.FromUInt32(0xFFFFFFFF),
            Selection: Color.FromUInt32(0xFFDDF1BC),
            SelectionForeground: Color.FromUInt32(0xFF1F2329)),
        Borders: new ThemeBorderPalette(
            Default: Color.FromUInt32(0xFFDFE1E5),
            Strong: Color.FromUInt32(0xFFC9CCD4),
            Disabled: Color.FromUInt32(0xFFE7E8EB),
            Focus: Color.FromUInt32(0xFF5F940F)),
        Status: new ThemeStatusPalette(
            Info: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFF315FBA),
                Surface: Color.FromUInt32(0xFFE8F1FF),
                Border: Color.FromUInt32(0xFF9BBDF2)),
            Success: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFF217A5A),
                Surface: Color.FromUInt32(0xFFE5F5EE),
                Border: Color.FromUInt32(0xFF8DCEB6)),
            Warning: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFF8A5A00),
                Surface: Color.FromUInt32(0xFFFFF4D6),
                Border: Color.FromUInt32(0xFFE5C46F)),
            Danger: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFFC2372A),
                Surface: Color.FromUInt32(0xFFFDEBE9),
                Border: Color.FromUInt32(0xFFE7A49D))));
}
