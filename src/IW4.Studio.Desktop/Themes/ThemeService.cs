using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    private readonly Application _application;
    private readonly AppSettingsStore _settingsStore;
    private readonly FluentTheme? _fluentTheme;
    private readonly Dictionary<Uri, Bitmap> _bannerImages = [];

    public ThemeService(Application application, AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settingsStore);

        _application = application;
        _settingsStore = settingsStore;
        _fluentTheme = application.Styles.OfType<FluentTheme>().SingleOrDefault();

        ConfigureFluentAccent(DarkThemeInstance);
        ConfigureFluentAccent(LightThemeInstance);

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
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private void Apply(ITheme theme)
    {
        ThemePalette palette = theme.Palette;

        _application.RequestedThemeVariant = theme.BaseVariant;
        _application.Resources["StudioFontFamily"] = theme.FontFamily;
        _application.Resources["StudioBannerImage"] = GetBannerImage(theme.BannerResource);

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

    private Bitmap GetBannerImage(Uri resource)
    {
        if (_bannerImages.TryGetValue(resource, out Bitmap? image))
            return image;

        using Stream stream = AssetLoader.Open(resource);
        image = new Bitmap(stream);
        _bannerImages.Add(resource, image);
        return image;
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
