using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Themes.Design;

/// <summary>
/// A Rider-inspired dark theme customized with the IW4 Studio green accent.
/// </summary>
public sealed class DarkTheme : ITheme
{
    public string Name => "IW4 Studio Dark";

    public ThemeMode Mode => ThemeMode.Dark;

    public ThemeVariant BaseVariant => ThemeVariant.Dark;

    public FontFamily FontFamily { get; } = new("Inter");

    public Uri BannerResource { get; } = new("avares://IW4%20Studio/Resources/banner_dark.png");

    public ThemePalette Palette { get; } = new(
        Accent: new ThemeAccentPalette(
            Default: Color.FromUInt32(0xFF8BD21C),
            Hovered: Color.FromUInt32(0xFF9DE13B),
            Pressed: Color.FromUInt32(0xFF76B716),
            Subtle: Color.FromUInt32(0xFF30411D),
            Foreground: Color.FromUInt32(0xFF8BD21C),
            OnAccent: Color.FromUInt32(0xFF172006),
            Secondary: Color.FromUInt32(0xFF5F940F),
            SecondarySubtle: Color.FromUInt32(0xFF27381A),
            SecondaryForeground: Color.FromUInt32(0xFF9DE13B),
            OnSecondary: Color.FromUInt32(0xFF172006)),
        Surfaces: new ThemeSurfacePalette(
            Canvas: Color.FromUInt32(0xFF1E1F22),
            Default: Color.FromUInt32(0xFF2B2D30),
            Raised: Color.FromUInt32(0xFF393B40),
            Sunken: Color.FromUInt32(0xFF18191C),
            Overlay: Color.FromUInt32(0xFF2B2D30),
            Sidebar: Color.FromUInt32(0xFF25262A),
            Scrim: Color.FromUInt32(0xE6191A1D)),
        Text: new ThemeTextPalette(
            Primary: Color.FromUInt32(0xFFDFE1E5),
            Secondary: Color.FromUInt32(0xFF9DA0A8),
            Tertiary: Color.FromUInt32(0xFF90949C),
            Disabled: Color.FromUInt32(0xFF5F6269)),
        Controls: new ThemeControlPalette(
            Default: Color.FromUInt32(0xFF2B2D30),
            Hovered: Color.FromUInt32(0xFF393B40),
            Pressed: Color.FromUInt32(0xFF43454A),
            Selected: Color.FromUInt32(0xFF354522),
            Disabled: Color.FromUInt32(0xFF25262A),
            Input: Color.FromUInt32(0xFF18191C),
            Selection: Color.FromUInt32(0xFF46621F),
            SelectionForeground: Color.FromUInt32(0xFFF2F4F7)),
        Borders: new ThemeBorderPalette(
            Default: Color.FromUInt32(0xFF393B40),
            Strong: Color.FromUInt32(0xFF4E5157),
            Disabled: Color.FromUInt32(0xFF303136),
            Focus: Color.FromUInt32(0xFF8BD21C)),
        Status: new ThemeStatusPalette(
            Info: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFF6EA8FE),
                Surface: Color.FromUInt32(0xFF26344A),
                Border: Color.FromUInt32(0xFF375A8C)),
            Success: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFF5ECFA4),
                Surface: Color.FromUInt32(0xFF203C33),
                Border: Color.FromUInt32(0xFF356B58)),
            Warning: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFFE2B86B),
                Surface: Color.FromUInt32(0xFF41351F),
                Border: Color.FromUInt32(0xFF70572B)),
            Danger: new ThemeStatusColor(
                Foreground: Color.FromUInt32(0xFFF07178),
                Surface: Color.FromUInt32(0xFF44282B),
                Border: Color.FromUInt32(0xFF7A3E43))));
}
