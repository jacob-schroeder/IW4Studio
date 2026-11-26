using Avalonia;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using IW4.Studio.Desktop.Persistence;
using IW4.Studio.Desktop.Themes.Design;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// Applies the selected semantic palette to application-level Avalonia resources.
/// </summary>
internal sealed class ThemeService
{
    private static readonly ITheme DarkThemeInstance = new DarkTheme();
    private static readonly ITheme LightThemeInstance = new LightTheme();
    private static readonly ITheme DarkYellowThemeInstance = new DarkYellowTheme();
    private static readonly ITheme LightYellowThemeInstance = new LightYellowTheme();

    private readonly Application _application;
    private readonly AppSettingsStore _settingsStore;
    private readonly FluentTheme? _fluentTheme;

    public ThemeService(Application application, AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settingsStore);

        _application = application;
        _settingsStore = settingsStore;
        _fluentTheme = application.Styles.OfType<FluentTheme>().SingleOrDefault();

        CurrentTheme = Resolve(settingsStore.LoadTheme());
        Apply(CurrentTheme);
    }

    public ITheme CurrentTheme { get; private set; }

    public void SelectTheme(ThemeMode mode)
    {
        _settingsStore.SaveTheme(mode);

        if (CurrentTheme.Mode == mode)
            return;

        CurrentTheme = Resolve(mode);
        Apply(CurrentTheme);
    }

    private static ITheme Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => DarkThemeInstance,
        ThemeMode.Light => LightThemeInstance,
        ThemeMode.DarkYellow => DarkYellowThemeInstance,
        ThemeMode.LightYellow => LightYellowThemeInstance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private void Apply(ITheme theme)
    {
        ThemePalette palette = theme.Palette;

        ConfigureFluentAccent(theme);
        _application.RequestedThemeVariant = theme.BaseVariant;
        _application.Resources["StudioFontFamily"] = theme.FontFamily;
        _application.Resources["StudioWindowGradientBrush"] = CreateWindowGradient(theme.Mode);

        SetBrush("StudioBackgroundBrush", palette.Surfaces.Canvas);
        SetBrush("StudioSurfaceBrush", palette.Surfaces.Default);
        SetBrush("StudioSurfaceRaisedBrush", palette.Surfaces.Raised);
        SetBrush("StudioSurfaceSunkenBrush", palette.Surfaces.Sunken);
        SetBrush("StudioSurfaceOverlayBrush", palette.Surfaces.Overlay);
        SetBrush("StudioSidebarBrush", palette.Surfaces.Sidebar);
        SetBrush("StudioScrimBrush", palette.Surfaces.Scrim);

        SetBrush("StudioTextBrush", palette.Text.Primary);
        SetBrush("StudioTextMutedBrush", palette.Text.Secondary);
        SetBrush("StudioTextSubtleBrush", palette.Text.Tertiary);
        SetBrush("StudioTextDisabledBrush", palette.Text.Disabled);

        SetBrush("StudioAccentBrush", palette.Accent.Foreground);
        SetBrush("StudioAccentStrongBrush", palette.Accent.Default);
        SetBrush("StudioAccentHoverBrush", palette.Accent.Hovered);
        SetBrush("StudioAccentPressedBrush", palette.Accent.Pressed);
        SetBrush("StudioAccentSurfaceBrush", palette.Accent.Subtle);
        SetBrush("StudioOnAccentBrush", palette.Accent.OnAccent);
        SetBrush("StudioAccentSecondaryBrush", palette.Accent.Secondary);
        SetBrush("StudioAccentSecondarySurfaceBrush", palette.Accent.SecondarySubtle);
        SetBrush("StudioAccentSecondaryForegroundBrush", palette.Accent.SecondaryForeground);
        SetBrush("StudioOnAccentSecondaryBrush", palette.Accent.OnSecondary);

        SetBrush("StudioControlBrush", palette.Controls.Default);
        SetBrush("StudioControlHoverBrush", palette.Controls.Hovered);
        SetBrush("StudioControlPressedBrush", palette.Controls.Pressed);
        SetBrush("StudioControlSelectedBrush", palette.Controls.Selected);
        SetBrush("StudioControlDisabledBrush", palette.Controls.Disabled);
        SetBrush("StudioInputBrush", palette.Controls.Input);
        SetBrush("StudioSelectionBrush", palette.Controls.Selection);
        SetBrush("StudioSelectionTextBrush", palette.Controls.SelectionForeground);

        SetBrush("StudioBorderBrush", palette.Borders.Default);
        SetBrush("StudioBorderStrongBrush", palette.Borders.Strong);
        SetBrush("StudioBorderDisabledBrush", palette.Borders.Disabled);
        SetBrush("StudioFocusBrush", palette.Borders.Focus);

        SetStatusBrushes("Info", palette.Status.Info);
        SetStatusBrushes("Success", palette.Status.Success);
        SetStatusBrushes("Warning", palette.Status.Warning);
        SetStatusBrushes("Error", palette.Status.Danger);
    }

    private void SetStatusBrushes(string name, ThemeStatusColor status)
    {
        SetBrush($"Studio{name}Brush", status.Foreground);
        SetBrush($"Studio{name}SurfaceBrush", status.Surface);
        SetBrush($"Studio{name}BorderBrush", status.Border);
    }

    private void SetBrush(string resourceName, Color color) =>
        _application.Resources[resourceName] = new SolidColorBrush(color);

    private static RadialGradientBrush CreateWindowGradient(ThemeMode mode)
    {
        (Color start, Color middle, Color end) = mode switch
        {
            ThemeMode.Dark => (Color.FromUInt32(0xFF666B35), Color.FromUInt32(0xFF3A5033), Color.FromUInt32(0xFF232C27)),
            ThemeMode.Light => (Color.FromUInt32(0xFFE0E3A1), Color.FromUInt32(0xFFC7DCB4), Color.FromUInt32(0xFFE7ECE7)),
            ThemeMode.DarkYellow => (Color.FromUInt32(0xFF867039), Color.FromUInt32(0xFF544C32), Color.FromUInt32(0xFF2C2B25)),
            ThemeMode.LightYellow => (Color.FromUInt32(0xFFF5DA8D), Color.FromUInt32(0xFFE0D8AF), Color.FromUInt32(0xFFEEECE4)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        return new RadialGradientBrush
        {
            Center = new RelativePoint(0.11, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.11, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.86, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.35, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(start, 0),
                new GradientStop(middle, 0.48),
                new GradientStop(end, 1)
            ]
        };
    }

    private void ConfigureFluentAccent(ITheme theme)
    {
        if (_fluentTheme is null)
            return;

        if (!_fluentTheme.Palettes.TryGetValue(theme.BaseVariant, out ColorPaletteResources? resources))
        {
            resources = new ColorPaletteResources();
            _fluentTheme.Palettes.Add(theme.BaseVariant, resources);
        }

        resources.Accent = theme.Palette.Accent.Default;
    }
}
