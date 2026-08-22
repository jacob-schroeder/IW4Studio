using System.Text;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class FontViewerViewModel
    : ObservableObject,
      IAssetEditorProperties
{
    private const double DefaultPreviewScale = 0.4;
    private const int GlyphsPerLine = 48;

    private readonly MenuNodeId _previewNodeId = MenuNodeId.New();
    private readonly string _defaultPreviewText;
    private string _previewText;
    private double _previewScale = DefaultPreviewScale;
    private MenuPreviewScene _previewScene;
    private MenuPreviewMaterialStatus? _materialStatus;
    private MenuPreviewTextStatus? _textStatus;

    public FontViewerViewModel(
        FontAsset font,
        IMenuPreviewMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(font);
        MaterialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));
        TextResourceResolver = new FontPreviewTextResourceResolver(font);

        Name = string.IsNullOrWhiteSpace(font.Name)
            ? "<unnamed font>"
            : font.Name;
        PixelHeightText = $"{font.PixelHeight:N0} px";
        GlyphCountText = font.GlyphCount == font.Glyphs.Count
            ? $"{font.Glyphs.Count:N0}"
            : $"{font.Glyphs.Count:N0} loaded / {font.GlyphCount:N0} declared";
        int previewableGlyphCount = font.Glyphs
            .Select(glyph => (char)glyph.Letter)
            .Where(IsPreviewableGlyph)
            .Distinct()
            .Count();
        GlyphCoverageText = $"{previewableGlyphCount:N0} previewable characters";
        MaterialName = font.Material?.Info.Name ?? "<unresolved>";
        GlowMaterialName = font.GlowMaterial?.Info.Name ?? "<none>";

        _defaultPreviewText = BuildDefaultPreviewText(font);
        _previewText = _defaultPreviewText;
        _previewScene = BuildPreviewScene();
    }

    public string Name { get; }

    public string PixelHeightText { get; }

    public string GlyphCountText { get; }

    public string GlyphCoverageText { get; }

    public string MaterialName { get; }

    public string GlowMaterialName { get; }

    public string PropertySectionName => "FONT DATA";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Height", PixelHeightText),
        new("Glyphs", GlyphCountText),
        new("Coverage", GlyphCoverageText),
        new("Material", MaterialName),
        new("Glow material", GlowMaterialName),
        new("Storage", "Raster glyph atlas and metrics"),
        new("Source font", "OTF/TTF outlines are not stored")
    ];

    public IMenuPreviewMaterialResolver MaterialResolver { get; }

    public IMenuTextResourceResolver TextResourceResolver { get; }

    public string PreviewText
    {
        get => _previewText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _previewText, value))
                return;

            RebuildPreview();
        }
    }

    public double PreviewScale
    {
        get => _previewScale;
        set
        {
            double normalized = double.IsFinite(value)
                ? Math.Clamp(value, 0.1, 1.0)
                : DefaultPreviewScale;
            if (!SetProperty(ref _previewScale, normalized))
                return;

            OnPropertyChanged(nameof(PreviewScaleText));
            RebuildPreview();
        }
    }

    public string PreviewScaleText => $"{PreviewScale:0.00}×";

    public MenuPreviewScene PreviewScene => _previewScene;

    public string PreviewStatus
    {
        get
        {
            if (_textStatus is { UsesGameGlyphs: false })
                return "IW4 glyph data unavailable — showing fallback text";
            if (_materialStatus is { IsResolved: false })
                return "Font atlas unavailable — showing fallback text";
            if (_textStatus is { UsesGameGlyphs: true } &&
                _materialStatus is { IsResolved: true })
            {
                return "IW4 glyph metrics and font atlas";
            }
            if (_textStatus is { UsesGameGlyphs: true })
                return "Loading font atlas…";

            return "Preparing IW4 font preview…";
        }
    }

    public string PreviewDetails
    {
        get
        {
            string[] details =
            [
                .. new[]
                {
                    _textStatus is { Diagnostics.Count: > 0 }
                        ? _textStatus.Detail
                        : null,
                    _materialStatus?.Detail
                }.Where(value => !string.IsNullOrWhiteSpace(value))
                 .Select(value => value!)
            ];
            return details.Length == 0
                ? PreviewStatus
                : string.Join(Environment.NewLine, details);
        }
    }

    public void ResetPreviewText()
    {
        PreviewText = _defaultPreviewText;
        PreviewScale = DefaultPreviewScale;
    }

    internal void ReportMaterialPreviewStatus(
        MenuPreviewMaterialStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_materialStatus == status)
            return;

        _materialStatus = status;
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    internal void ReportTextPreviewStatus(MenuPreviewTextStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_textStatus == status)
            return;

        _textStatus = status;
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    private void RebuildPreview()
    {
        _materialStatus = null;
        _textStatus = null;
        _previewScene = BuildPreviewScene();
        OnPropertyChanged(nameof(PreviewScene));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    private MenuPreviewScene BuildPreviewScene()
    {
        MenuPreviewSettings settings = MenuPreviewSettings.Default;
        var virtualBounds = new MenuRectangleValue(
            24,
            20,
            592,
            440,
            HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT,
            VerticalAlign.VERTICAL_ALIGN_SUBTOP);
        var previewText = new MenuPreviewText(
            _previewNodeId,
            MenuRectTransform.Place(virtualBounds, settings),
            ZIndex: 0,
            PreviewText,
            new MenuColorValue(1, 1, 1, 1),
            (float)PreviewScale,
            Font: 7,
            Alignment: 4,
            Style: 0,
            OffsetX: 0,
            OffsetY: 0,
            BorderInset: 0);
        return new MenuPreviewScene(
            settings,
            [previewText],
            [],
            []);
    }

    private static string BuildDefaultPreviewText(FontAsset font)
    {
        char[] glyphs = font.Glyphs
            .Select(glyph => (char)glyph.Letter)
            .Where(IsPreviewableGlyph)
            .Distinct()
            .ToArray();
        var text = new StringBuilder(
            "The quick brown fox jumps over the lazy dog.\n");
        for (int index = 0; index < glyphs.Length; index++)
        {
            if (index > 0 && index % GlyphsPerLine == 0)
                text.AppendLine();
            text.Append(glyphs[index]);
        }

        return text.ToString();
    }

    private static bool IsPreviewableGlyph(char value) =>
        value is not ('\0' or '\r' or '\n') &&
        !char.IsSurrogate(value);

    private sealed class FontPreviewTextResourceResolver(FontAsset font)
        : IMenuTextResourceResolver
    {
        private static readonly MenuTextResourceRevision StableRevision =
            new(0, 0);

        public MenuTextResourceRevision Revision => StableRevision;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public MenuLocalizedTextResolution ResolveText(string authoredText)
        {
            ArgumentNullException.ThrowIfNull(authoredText);
            return MenuLocalizedTextResolution.Literal(
                authoredText,
                StableRevision);
        }

        public MenuFontAssetResolution ResolveFont(
            int fontEnum,
            MenuFontSelectionContext? context = null)
        {
            MenuFontEnumResolution mapping =
                MenuFontEnumResolution.Known(
                    fontEnum,
                    MenuFontRole.Normal,
                    string.IsNullOrWhiteSpace(font.Name)
                        ? "<unnamed font>"
                        : font.Name);
            return MenuFontAssetResolution.Resolved(
                mapping,
                font,
                StableRevision);
        }
    }
}
