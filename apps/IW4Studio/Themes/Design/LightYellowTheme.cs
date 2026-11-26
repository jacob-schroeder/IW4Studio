using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Themes.Design;

public sealed class LightYellowTheme : ITheme
{
    private static readonly ThemePalette GreenPalette = new LightTheme().Palette;

    public string Name => "IW4 Studio Light Yellow";

    public ThemeMode Mode => ThemeMode.LightYellow;

    public ThemeVariant BaseVariant => ThemeVariant.Light;

    public FontFamily FontFamily { get; } = new("Inter");

    public ThemePalette Palette { get; } = GreenPalette with
    {
        Accent = GreenPalette.Accent with
        {
            Default = Color.FromUInt32(0xFFE5AA3D),
            Hovered = Color.FromUInt32(0xFFD7962B),
            Pressed = Color.FromUInt32(0xFFBE7D20),
            Subtle = Color.FromUInt32(0xFFFFF2D8),
            Foreground = Color.FromUInt32(0xFF8C5A12),
            OnAccent = Color.FromUInt32(0xFF2B210E),
            Secondary = Color.FromUInt32(0xFFAD7522),
            SecondarySubtle = Color.FromUInt32(0xFFF9E9C8),
            SecondaryForeground = Color.FromUInt32(0xFF805114),
            OnSecondary = Color.FromUInt32(0xFF2B210E)
        },
        Controls = GreenPalette.Controls with
        {
            Selected = Color.FromUInt32(0xFFF9E9C8),
            Selection = Color.FromUInt32(0xFFF3D99E)
        },
        Borders = GreenPalette.Borders with
        {
            Focus = Color.FromUInt32(0xFFAD7522)
        }
    };
}
