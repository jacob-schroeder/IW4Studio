using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Themes.Design;

public sealed class DarkYellowTheme : ITheme
{
    private static readonly ThemePalette GreenPalette = new DarkTheme().Palette;

    public string Name => "IW4 Studio Dark Yellow";

    public ThemeMode Mode => ThemeMode.DarkYellow;

    public ThemeVariant BaseVariant => ThemeVariant.Dark;

    public FontFamily FontFamily { get; } = new("Inter");

    public ThemePalette Palette { get; } = GreenPalette with
    {
        Accent = GreenPalette.Accent with
        {
            Default = Color.FromUInt32(0xFFE6AE47),
            Hovered = Color.FromUInt32(0xFFF1C16C),
            Pressed = Color.FromUInt32(0xFFCB9131),
            Subtle = Color.FromUInt32(0xFF433720),
            Foreground = Color.FromUInt32(0xFFF0BE64),
            OnAccent = Color.FromUInt32(0xFF241B0D),
            Secondary = Color.FromUInt32(0xFFAD7E31),
            SecondarySubtle = Color.FromUInt32(0xFF382F20),
            SecondaryForeground = Color.FromUInt32(0xFFF1C16C),
            OnSecondary = Color.FromUInt32(0xFF241B0D)
        },
        Controls = GreenPalette.Controls with
        {
            Selected = Color.FromUInt32(0xFF4B3B22),
            Selection = Color.FromUInt32(0xFF71512A)
        },
        Borders = GreenPalette.Borders with
        {
            Focus = Color.FromUInt32(0xFFE6AE47)
        }
    };
}
