using System.Text;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Font;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class FontViewerViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState
{
    private const double DefaultPreviewScale = 0.4;
    private const int GlyphsPerLine = 48;

    private readonly AssetEditorSession _session;
    private readonly IMenuPreviewMaterialResolver _workspaceMaterialResolver;
    private readonly MenuNodeId _previewNodeId = MenuNodeId.New();
    private FontAsset _font;
    private FontAssemblyCompileResult? _compiledCandidate;
    private string? _replacementSource;
    private string _defaultPreviewText;
    private string _previewText;
    private double _previewScale = DefaultPreviewScale;
    private MenuPreviewScene _previewScene;
    private IMenuPreviewMaterialResolver _materialResolver;
    private IMenuTextResourceResolver _textResourceResolver;
    private MenuPreviewMaterialStatus? _materialStatus;
    private MenuPreviewTextStatus? _textStatus;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _statusMessage = string.Empty;
    private long _previewResourceRevision;

    public FontViewerViewModel(
        AssetEditorSession session,
        IMenuPreviewMaterialResolver materialResolver)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.Font)
            throw new InvalidDataException("The Font view model can host only Font editor sessions.");
        _workspaceMaterialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));

        _font = session.OpenDraft<FontDraft>().Font;
        _defaultPreviewText = BuildDefaultPreviewText(_font);
        _previewText = _defaultPreviewText;
        _materialResolver = CreateMaterialResolver(_font);
        _textResourceResolver = new FontPreviewTextResourceResolver(_font);
        _previewScene = BuildPreviewScene();
    }

    public WorkspaceAssetAccess Mode => _session.Mode;

    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;

    public bool IsReadOnly => !IsEditable;

    public string Name => string.IsNullOrWhiteSpace(_font.Name)
        ? "<unnamed font>"
        : _font.Name;

    public string PixelHeightText => $"{_font.PixelHeight:N0} px";

    public string GlyphCountText => _font.GlyphCount == _font.Glyphs.Count
        ? $"{_font.Glyphs.Count:N0}"
        : $"{_font.Glyphs.Count:N0} loaded / {_font.GlyphCount:N0} declared";

    public string GlyphCoverageText
    {
        get
        {
            int count = _font.Glyphs
                .Select(glyph => (char)glyph.Letter)
                .Where(IsPreviewableGlyph)
                .Distinct()
                .Count();
            return $"{count:N0} previewable characters";
        }
    }

    public string MaterialName => _font.Material?.Info.Name ?? "<unresolved>";

    public string GlowMaterialName => _font.GlowMaterial?.Info.Name ?? "<none>";

    public string SourceFontText => string.IsNullOrWhiteSpace(_replacementSource)
        ? "OTF/TTF outlines are not stored"
        : _replacementSource;

    public string PropertySectionName => "FONT DATA";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Height", PixelHeightText),
        new("Glyphs", GlyphCountText),
        new("Coverage", GlyphCoverageText),
        new("Material", MaterialName),
        new("Glow material", GlowMaterialName),
        new("Storage", "Raster glyph atlas and metrics"),
        new("Source font", SourceFontText)
    ];

    public IReadOnlyList<AssetValidationIssue> Diagnostics => _diagnostics;

    public bool HasUnappliedChanges => _compiledCandidate is not null;

    public bool CanReplace => IsEditable;

    public bool CanApply => IsEditable && _compiledCandidate?.IsSuccess == true;

    public bool CanRevert => IsEditable &&
        (HasUnappliedChanges || _session.HasUnsavedChanges);

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!SetProperty(ref _statusMessage, value))
                return;
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public IMenuPreviewMaterialResolver MaterialResolver => _materialResolver;

    public IMenuTextResourceResolver TextResourceResolver => _textResourceResolver;

    public string PreviewText
    {
        get => _previewText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _previewText, value))
                return;

            RebuildPreviewScene();
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
            RebuildPreviewScene();
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
                return HasUnappliedChanges
                    ? "Compiled IW4 candidate — not yet applied"
                    : "IW4 glyph metrics and font atlas";
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

    internal FontReplacementCandidate CompileReplacement(
        ReadOnlyMemory<byte> sourceBytes)
    {
        if (!IsEditable)
            throw new InvalidOperationException("This Font is read-only.");

        FontAsset template = _session.OpenDraft<FontDraft>().Font;
        OpenTypeFontRasterization rasterized =
            OpenTypeFontRasterizer.Rasterize(sourceBytes, template);
        FontAssemblyCompileResult compiled = FontAssemblyCompiler.Compile(
            template,
            rasterized.Rasterization);
        return new FontReplacementCandidate(
            compiled,
            rasterized.FamilyName,
            rasterized.SubstitutedGlyphCount,
            rasterized.Rasterization.AtlasWidth,
            rasterized.Rasterization.AtlasHeight);
    }

    internal bool TryStageReplacement(
        FontReplacementCandidate candidate,
        string source,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        error = null;
        if (!IsEditable)
        {
            error = "This Font is read-only.";
            return false;
        }

        SetDiagnostics(candidate.Compiled.Issues);
        if (!candidate.Compiled.IsSuccess)
        {
            error = string.Join(
                " ",
                candidate.Compiled.Issues
                    .Where(issue => issue.Severity == AssetValidationSeverity.Error)
                    .Take(3)
                    .Select(issue => issue.Message));
            StatusMessage = string.IsNullOrWhiteSpace(error)
                ? "Font replacement compilation was blocked."
                : $"Font replacement compilation blocked: {error}";
            return false;
        }

        _compiledCandidate = candidate.Compiled;
        _replacementSource = string.IsNullOrWhiteSpace(source)
            ? "OpenType import"
            : source;
        UseFont(candidate.Compiled.Definition);
        string substitutions = candidate.SubstitutedGlyphCount == 0
            ? string.Empty
            : $" {candidate.SubstitutedGlyphCount:N0} unavailable source glyph(s) use the native '.' fallback.";
        StatusMessage =
            $"Staged {candidate.FamilyName} as a {candidate.AtlasWidth:N0}×{candidate.AtlasHeight:N0} IW4 atlas; " +
            $"review the preview, then Apply.{substitutions}";
        NotifyEditingStateChanged();
        return true;
    }

    public bool ApplyCompiledDraft()
    {
        if (!CanApply || _compiledCandidate is null)
            return false;

        bool applied;
        IReadOnlyList<AssetValidationIssue> issues;
        try
        {
            applied = _session.ApplyCompiledFont(
                _compiledCandidate.Definition,
                _compiledCandidate.Providers,
                out issues);
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   InvalidOperationException or
                   ArgumentException or
                   OverflowException)
        {
            SetDiagnostics(
                [new AssetValidationIssue(
                    "font.apply",
                    exception.Message,
                    AssetValidationSeverity.Error)]);
            StatusMessage = $"Font Apply blocked: {exception.Message}";
            return false;
        }

        SetDiagnostics(issues);
        if (!applied)
        {
            _compiledCandidate = null;
            _replacementSource = null;
            LoadCurrentFont();
            StatusMessage = "The compiled Font already matches the applied asset.";
            NotifyEditingStateChanged();
            return false;
        }

        _compiledCandidate = null;
        _replacementSource = null;
        LoadCurrentFont();
        StatusMessage =
            "Applied the Font, cloned normal/glow Materials, and inline atlas Image atomically.";
        NotifyEditingStateChanged();
        return true;
    }

    public void RevertDraft()
    {
        if (!CanRevert)
            return;

        if (HasUnappliedChanges)
        {
            _compiledCandidate = null;
            _replacementSource = null;
            SetDiagnostics([]);
            LoadCurrentFont();
            StatusMessage = "Discarded the staged OpenType replacement.";
            NotifyEditingStateChanged();
            return;
        }

        bool reverted = _session.Revert();
        SetDiagnostics([]);
        LoadCurrentFont();
        StatusMessage = reverted
            ? "Reverted the Font and its owned Materials/Image to the saved baseline."
            : "The Font already matches its saved baseline.";
        NotifyEditingStateChanged();
    }

    public void ReportReplacementFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        SetDiagnostics(
            [new AssetValidationIssue(
                "font.import",
                message,
                AssetValidationSeverity.Error)]);
        StatusMessage = $"Font replacement failed: {message}";
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

    private void LoadCurrentFont()
    {
        _font = _session.OpenDraft<FontDraft>().Font;
        UseFont(_font);
    }

    private void UseFont(FontAsset font)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _defaultPreviewText = BuildDefaultPreviewText(font);
        _materialResolver = CreateMaterialResolver(font);
        _textResourceResolver = new FontPreviewTextResourceResolver(font);
        _materialStatus = null;
        _textStatus = null;
        _previewScene = BuildPreviewScene();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PixelHeightText));
        OnPropertyChanged(nameof(GlyphCountText));
        OnPropertyChanged(nameof(GlyphCoverageText));
        OnPropertyChanged(nameof(MaterialName));
        OnPropertyChanged(nameof(GlowMaterialName));
        OnPropertyChanged(nameof(SourceFontText));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(MaterialResolver));
        OnPropertyChanged(nameof(TextResourceResolver));
        OnPropertyChanged(nameof(PreviewScene));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    private IMenuPreviewMaterialResolver CreateMaterialResolver(FontAsset font) =>
        new FontPreviewMaterialResolver(
            _workspaceMaterialResolver,
            font,
            Interlocked.Increment(ref _previewResourceRevision));

    private void RebuildPreviewScene()
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

    private void SetDiagnostics(IEnumerable<AssetValidationIssue> issues)
    {
        _diagnostics = Array.AsReadOnly(issues
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
        OnPropertyChanged(nameof(Diagnostics));
    }

    private void NotifyEditingStateChanged()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(PreviewStatus));
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

internal sealed record FontReplacementCandidate(
    FontAssemblyCompileResult Compiled,
    string FamilyName,
    int SubstitutedGlyphCount,
    int AtlasWidth,
    int AtlasHeight);
