using Avalonia.Media.Imaging;
using IW4.AssetExchange.Image;
using IW4.AssetExchange.SourceFormat.Image;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;
using IW4.Render.UI;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Material;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Workbench.Composition;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class MaterialEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState,
      IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly WorkspaceGfxImagePayloadResolver _payloadResolver;
    private CancellationTokenSource? _previewCancellation;
    private long _previewRevision;
    private MaterialDraft _currentDraft;
    private MaterialImageImportCandidate? _stagedImport;
    private UiMaterialPreviewPlan _previewPlan;
    private Bitmap? _preview;
    private IReadOnlyList<MaterialPreviewMipViewModel> _previewMips = [];
    private MaterialPreviewMipViewModel? _selectedPreviewMip;
    private string _previewSummary = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _previewMessage = "Preparing Material preview…";
    private string _previewDetails = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isPreviewLoading;
    private bool _disposed;

    public MaterialEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.Material)
        {
            throw new InvalidDataException(
                "The Material view model can host only Material editor sessions.");
        }

        _payloadResolver = new WorkspaceGfxImagePayloadResolver(session.Workspace);
        _currentDraft = session.OpenDraft<MaterialDraft>();
        _previewPlan = UiMaterialPreviewPlanner.Plan(_currentDraft.Material);
        BeginPreviewLoad();
    }

    public WorkspaceAssetAccess Mode => _session.Mode;

    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;

    public bool IsReadOnly => !IsEditable;

    public string Name => string.IsNullOrWhiteSpace(ActiveMaterial.Info.Name)
        ? "<unnamed material>"
        : ActiveMaterial.Info.Name;

    public string TextureCountText =>
        $"{ActiveMaterial.Textures.Count:N0} " +
        (ActiveMaterial.Textures.Count == 1 ? "texture" : "textures");

    public string TechniqueSetName =>
        ActiveMaterial.TechniqueSet?.Name ?? "<unresolved>";

    public string SelectedImageName =>
        SelectedImage?.Name ?? "<unresolved>";

    public string SelectedTextureText => _previewPlan.SelectedTexture is { } selected
        ? $"{FormatIdentifier(selected.Role.ToString())} · row {selected.TextureTableOrdinal:N0}"
        : "No preview texture";

    public string ImageDimensionsText => SelectedImage is { } image
        ? FormatImageDimensions(image)
        : "Unavailable";

    public string ImageFormatText => SelectedImage is { } image
        ? $"{image.FormatEncoding.BaseFormat} · " +
          (image.FormatEncoding.IsLinear ? "linear" : "swizzled")
        : "Unavailable";

    public string MipCountText => SelectedImage is { } image
        ? $"{image.LevelCount:N0} " +
          (image.LevelCount == 1 ? "level" : "levels")
        : "Unavailable";

    public string AtlasText
    {
        get
        {
            byte rows = ActiveMaterial.Info.TextureAtlasRowCount;
            byte columns = ActiveMaterial.Info.TextureAtlasColumnCount;
            return rows == 0 && columns == 0
                ? "None"
                : $"{rows:N0} × {columns:N0}";
        }
    }

    public string PropertySectionName => "MATERIAL DATA";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Textures", TextureCountText),
        new("Preview row", SelectedTextureText),
        new("Preview image", SelectedImageName),
        new("Dimensions", ImageDimensionsText),
        new("Format", ImageFormatText),
        new("Mip levels", MipCountText),
        new("Technique set", TechniqueSetName),
        new("Atlas", AtlasText)
    ];

    public IReadOnlyList<AssetValidationIssue> Diagnostics => _diagnostics;

    public bool HasUnappliedChanges => _stagedImport is not null;

    public bool CanImport =>
        !_disposed &&
        IsEditable &&
        _previewPlan.SelectedTexture is { Image: not null } selected &&
        selected.TextureSemantic != TextureSemantic.WaterMap &&
        ActiveMaterial.Textures[selected.TextureTableOrdinal].Water is null;

    public bool CanApply => !_disposed && IsEditable && HasUnappliedChanges;

    public bool CanRevert =>
        !_disposed &&
        IsEditable &&
        (HasUnappliedChanges || _session.HasUnsavedChanges);

    public bool CanExport => !_disposed && SelectedImage is not null;

    public Bitmap? Preview
    {
        get => _preview;
        private set
        {
            Bitmap? previous = _preview;
            if (!SetProperty(ref _preview, value))
                return;

            previous?.Dispose();
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public bool HasPreview => Preview is not null;

    public IReadOnlyList<MaterialPreviewMipViewModel> PreviewMips =>
        _previewMips;

    public bool HasMipSelector => PreviewMips.Count > 1;

    public MaterialPreviewMipViewModel? SelectedPreviewMip
    {
        get => _selectedPreviewMip;
        set
        {
            if (value is not null && !PreviewMips.Contains(value))
                return;
            if (!SetProperty(ref _selectedPreviewMip, value))
                return;

            ShowSelectedPreviewMip();
        }
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetProperty(ref _isPreviewLoading, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public string PreviewDetails
    {
        get => _previewDetails;
        private set => SetProperty(ref _previewDetails, value);
    }

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

    internal bool TryCaptureImportTarget(
        out MaterialImageImportTarget? target)
    {
        target = null;
        if (!CanImport)
            return false;

        MaterialDraft draft = _session.OpenDraft<MaterialDraft>();
        UiMaterialPreviewPlan plan = UiMaterialPreviewPlanner.Plan(
            draft.Material);
        if (plan.SelectedTexture is not { Image: not null } selected ||
            selected.TextureSemantic == TextureSemantic.WaterMap ||
            draft.Material.Textures[selected.TextureTableOrdinal].Water is not null)
        {
            return false;
        }

        target = new MaterialImageImportTarget(
            draft,
            selected.TextureTableOrdinal);
        return true;
    }

    internal static MaterialImageImportCandidate CompileImport(
        MaterialImageImportTarget target,
        ImageFileDocument source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        return MaterialImportedImageCompiler.Compile(
            target.Draft,
            target.TextureTableOrdinal,
            source);
    }

    internal bool TryStageImport(
        MaterialImageImportCandidate candidate,
        string source,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        error = null;
        if (_disposed || !IsEditable)
        {
            error = _disposed
                ? "The Material editor is closed."
                : "This Material is read-only.";
            return false;
        }

        MaterialDraft current = _session.OpenDraft<MaterialDraft>();
        UiMaterialPreviewPlan currentPlan = UiMaterialPreviewPlanner.Plan(
            current.Material);
        if (currentPlan.SelectedTexture is not { } selected ||
            selected.TextureTableOrdinal != candidate.TextureTableOrdinal)
        {
            error = "The Material preview texture changed while the image was being imported.";
            ReportImportFailure(error);
            return false;
        }

        AssetEditorValidationState validation =
            _session.ValidateCandidate(candidate.Draft);
        SetDiagnostics(validation.Issues);
        if (validation.HasErrors)
        {
            error = string.Join(
                " ",
                validation.Issues
                    .Where(issue => issue.Severity == AssetValidationSeverity.Error)
                    .Take(3)
                    .Select(issue => issue.Message));
            StatusMessage = string.IsNullOrWhiteSpace(error)
                ? "Material image import was blocked."
                : $"Material image import blocked: {error}";
            return false;
        }

        _stagedImport = candidate;
        _currentDraft = candidate.Draft;
        string sourceName = string.IsNullOrWhiteSpace(source)
            ? "the imported image"
            : source;
        StatusMessage =
            $"Staged {sourceName} as {candidate.Width:N0} × {candidate.Height:N0} " +
            $"with {candidate.MipCount:N0} {(candidate.MipCount == 1 ? "mip" : "mips")}; " +
            "review the preview, then Apply Changes.";
        UseCurrentMaterial();
        NotifyEditingStateChanged();
        return true;
    }

    public bool ApplyChanges()
    {
        if (!CanApply || _stagedImport is not { } candidate)
            return false;

        bool applied;
        IReadOnlyList<AssetValidationIssue> issues;
        try
        {
            applied = _session.ApplyCompiledMaterial(
                candidate.Draft,
                [candidate.Image],
                out issues);
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   InvalidOperationException or
                   ArgumentException or
                   OverflowException)
        {
            SetDiagnostics(
            [
                new AssetValidationIssue(
                    "material.apply",
                    exception.Message,
                    AssetValidationSeverity.Error)
            ]);
            StatusMessage = $"Material Apply blocked: {exception.Message}";
            return false;
        }

        SetDiagnostics(issues);
        _stagedImport = null;
        LoadCurrentDraft();
        StatusMessage = applied
            ? "Applied the Material and its isolated inline Image provider atomically."
            : "The imported Material image already matches the applied asset.";
        NotifyEditingStateChanged();
        return applied;
    }

    public void RevertChanges()
    {
        if (!CanRevert)
            return;

        if (HasUnappliedChanges)
        {
            _stagedImport = null;
            SetDiagnostics([]);
            LoadCurrentDraft();
            StatusMessage = "Discarded the staged Material image import.";
            NotifyEditingStateChanged();
            return;
        }

        bool reverted = _session.Revert();
        SetDiagnostics([]);
        LoadCurrentDraft();
        StatusMessage = reverted
            ? "Reverted the Material and its owned imported Image to the saved baseline."
            : "The Material already matches its saved baseline.";
        NotifyEditingStateChanged();
    }

    internal bool TryCaptureExport(out MaterialImageExportTarget? target)
    {
        target = null;
        if (!CanExport || SelectedImage is not { } image)
            return false;

        target = new MaterialImageExportTarget(
            image,
            SuggestedExportName(Name));
        return true;
    }

    internal MaterialImageExportPayload CreateExport(
        MaterialImageExportTarget target,
        ImageFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<ImageSourceMipLevel> mipLevels =
            SourceImageDumpDecoder.Decode(target.Image, _payloadResolver);
        using var stream = new MemoryStream();
        new ImageExchange().Write(stream, format, target.Image, mipLevels);
        return new MaterialImageExportPayload(
            stream.ToArray(),
            target.SuggestedFileName);
    }

    internal void RefreshSessionState() =>
        OnPropertyChanged(nameof(CanRevert));

    public void ReportImportFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        SetDiagnostics(
        [
            new AssetValidationIssue(
                "material.import",
                message,
                AssetValidationSeverity.Error)
        ]);
        StatusMessage = $"Material image import failed: {message}";
    }

    public void ReportExportFailure(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            StatusMessage = $"Material image export failed: {message}";
    }

    public void ReportExportSuccess(string destination, ImageFileFormat format)
    {
        string label = format == ImageFileFormat.Iwi8 ? "IWI" : "DDS";
        StatusMessage = string.IsNullOrWhiteSpace(destination)
            ? $"Exported the selected Material texture as {label}."
            : $"Exported the selected Material texture as {label} to {destination}.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelPreviewLoad();
        Preview = null;
    }

    private MaterialAsset ActiveMaterial => _currentDraft.Material;

    private GfxImageAsset? SelectedImage => _previewPlan.SelectedImage;

    private void LoadCurrentDraft()
    {
        _currentDraft = _session.OpenDraft<MaterialDraft>();
        UseCurrentMaterial();
    }

    private void UseCurrentMaterial()
    {
        _previewPlan = UiMaterialPreviewPlanner.Plan(ActiveMaterial);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TextureCountText));
        OnPropertyChanged(nameof(TechniqueSetName));
        OnPropertyChanged(nameof(SelectedImageName));
        OnPropertyChanged(nameof(SelectedTextureText));
        OnPropertyChanged(nameof(ImageDimensionsText));
        OnPropertyChanged(nameof(ImageFormatText));
        OnPropertyChanged(nameof(MipCountText));
        OnPropertyChanged(nameof(AtlasText));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanExport));
        BeginPreviewLoad();
    }

    private void BeginPreviewLoad()
    {
        CancelPreviewLoad();
        Preview = null;
        _previewMips = [];
        _selectedPreviewMip = null;
        _previewSummary = string.Empty;
        OnPropertyChanged(nameof(PreviewMips));
        OnPropertyChanged(nameof(SelectedPreviewMip));
        OnPropertyChanged(nameof(HasMipSelector));
        IsPreviewLoading = true;
        PreviewMessage = "Loading Material texture preview…";
        PreviewDetails = BuildPreviewDetails(_previewPlan);
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        long revision = Interlocked.Increment(ref _previewRevision);
        MaterialAsset material = ActiveMaterial;
        _ = LoadPreviewAsync(material, revision, cancellation.Token);
    }

    private async Task LoadPreviewAsync(
        MaterialAsset material,
        long revision,
        CancellationToken cancellationToken)
    {
        MaterialPreviewLoadResult result;
        try
        {
            result = await Task.Run(
                () => MaterialPreviewLoadResult.Decode(
                    material,
                    _payloadResolver),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = MaterialPreviewLoadResult.Failed(
                $"Material preview could not be decoded: {exception.Message}",
                string.Empty);
        }

        if (_disposed ||
            cancellationToken.IsCancellationRequested ||
            revision != Volatile.Read(ref _previewRevision))
        {
            return;
        }

        IsPreviewLoading = false;
        PreviewMessage = result.Message;
        PreviewDetails = result.Details;
        if (result.Mips.Count == 0)
            return;

        _previewMips = Array.AsReadOnly(result.Mips
            .Select(mip => new MaterialPreviewMipViewModel(
                mip.Level,
                mip.Width,
                mip.Height,
                mip.Label,
                mip.PngBytes))
            .ToArray());
        _previewSummary = result.Message;
        OnPropertyChanged(nameof(PreviewMips));
        OnPropertyChanged(nameof(HasMipSelector));
        SelectedPreviewMip = _previewMips[0];
    }

    private void CancelPreviewLoad()
    {
        CancellationTokenSource? cancellation = _previewCancellation;
        _previewCancellation = null;
        if (cancellation is null)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
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
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private static string BuildPreviewDetails(UiMaterialPreviewPlan plan)
    {
        bool supportsVolume = plan.SelectedImage is { } image &&
            IsVolume(image);
        return string.Join(
            Environment.NewLine,
            plan.Diagnostics
                .Where(diagnostic =>
                    !supportsVolume ||
                    diagnostic.Code !=
                    UiMaterialPreviewDiagnosticCode.UnsupportedImageDepth)
                .Select(diagnostic => diagnostic.Message));
    }

    private static string FormatImageDimensions(GfxImageAsset image)
    {
        if (IsCube(image))
            return $"{image.Width:N0} × {image.Height:N0} × 6 faces";
        if (IsVolume(image))
            return $"{image.Width:N0} × {image.Height:N0} × {image.Depth:N0}";
        return $"{image.Width:N0} × {image.Height:N0}";
    }

    private static bool IsTwoDimensional(GfxImageAsset image) =>
        image.MapType == MapType.TwoDimensional &&
        image.DimensionCount == GfxImageDimension.TwoDimensional &&
        !image.IsCubemap &&
        image.Depth == 1;

    private static bool IsCube(GfxImageAsset image) =>
        image.MapType == MapType.Cube &&
        image.DimensionCount == GfxImageDimension.TwoDimensional &&
        image.IsCubemap &&
        image.Depth == 1;

    private static bool IsVolume(GfxImageAsset image) =>
        image.MapType == MapType.ThreeDimensional &&
        image.DimensionCount == GfxImageDimension.ThreeDimensional &&
        !image.IsCubemap &&
        image.Depth > 0;

    private static string SuggestedExportName(string materialName)
    {
        string leaf = materialName.Replace('\\', '/').Split('/').LastOrDefault()
            ?? "material";
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(leaf
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "material" : safe;
    }

    private static string FormatIdentifier(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(value[index - 1]) ||
                 index + 1 < value.Length && char.IsLower(value[index + 1])))
            {
                result.Append(' ');
            }
            result.Append(current);
        }
        return result.ToString();
    }

    private void ShowSelectedPreviewMip()
    {
        MaterialPreviewMipViewModel? selected = SelectedPreviewMip;
        if (selected is null)
        {
            Preview = null;
            return;
        }

        try
        {
            using var stream = new MemoryStream(
                selected.PngBytes,
                writable: false);
            Preview = new Bitmap(stream);
            PreviewMessage = string.IsNullOrWhiteSpace(_previewSummary)
                ? selected.DisplayName
                : $"{_previewSummary} · {selected.DisplayName}";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Preview = null;
            PreviewMessage =
                $"Material mip preview could not be displayed: {exception.Message}";
        }
    }

    private sealed record MaterialPreviewLoadResult(
        IReadOnlyList<MaterialPreviewMipData> Mips,
        string Message,
        string Details)
    {
        internal static MaterialPreviewLoadResult Decode(
            MaterialAsset material,
            WorkspaceGfxImagePayloadResolver payloadResolver)
        {
            UiMaterialPreviewPlan plan = UiMaterialPreviewPlanner.Plan(material);
            string details = BuildPreviewDetails(plan);
            if (plan.SelectedImage is not { } image)
            {
                string reason = string.Join(
                    " ",
                    plan.Blockers.Take(2).Select(blocker => blocker.Message));
                return Failed(
                    string.IsNullOrWhiteSpace(reason)
                        ? "This Material has no previewable texture."
                        : reason,
                    details);
            }

            bool isTwoDimensional = IsTwoDimensional(image);
            bool isCube = IsCube(image);
            bool isVolume = IsVolume(image);
            if (!isTwoDimensional && !isCube && !isVolume)
            {
                return Failed(
                    $"Material preview does not support image shape " +
                    $"{image.MapType}/{image.DimensionCount} " +
                    $"(cubemap={image.IsCubemap}, depth={image.Depth}).",
                    details);
            }

            UiMaterialPreviewDiagnostic[] blockers = plan.Blockers
                .Where(blocker =>
                    !isVolume ||
                    blocker.Code !=
                    UiMaterialPreviewDiagnosticCode.UnsupportedImageDepth)
                .ToArray();
            if (blockers.Length != 0)
            {
                string reason = string.Join(
                    " ",
                    blockers.Take(2).Select(blocker => blocker.Message));
                return Failed(
                    string.IsNullOrWhiteSpace(reason)
                        ? "This Material has no previewable texture."
                        : reason,
                    details);
            }

            string role = plan.SelectedTexture is { } selected
                ? FormatIdentifier(selected.Role.ToString())
                : "Selected texture";
            try
            {
                IReadOnlyList<ImageSourceMipLevel> levels =
                    SourceImageDumpDecoder.Decode(image, payloadResolver);
                MaterialPreviewMipData[] mips = levels
                    .Select((level, index) => CreatePreviewMip(
                        level,
                        index,
                        isCube,
                        isVolume))
                    .ToArray();
                int completeMipCount = ComputeFullMipCount(
                    levels[0].Width,
                    levels[0].Height,
                    isVolume ? levels[0].Depth : 1);
                string chainStatus = levels.Count == 1
                    ? "single mip"
                    : levels.Count == completeMipCount
                        ? "complete chain"
                        : "partial chain";
                string shape = isCube
                    ? "6-face cubemap sheet"
                    : isVolume
                        ? "volume slice sheet"
                        : "2D texture";
                return new MaterialPreviewLoadResult(
                    Array.AsReadOnly(mips),
                    $"{role} · {image.Name ?? "<unnamed image>"} · " +
                    $"{shape} · {levels.Count:N0} " +
                    $"{(levels.Count == 1 ? "mip" : "mips")} · " +
                    $"{chainStatus} · texture approximation",
                    details);
            }
            catch (Exception chainException) when (chainException is not
                       OutOfMemoryException)
            {
                if (isCube || isVolume)
                {
                    string shape = isCube ? "cubemap" : "volume texture";
                    return Failed(
                        $"Material {shape} preview is unavailable: " +
                        chainException.Message,
                        details);
                }

                if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                        image,
                        payloadResolver,
                        out GfxImagePreviewSnapshot? preview,
                        out string failure) ||
                    preview is null)
                {
                    return Failed(
                        $"Material texture preview is unavailable: {failure}",
                        string.Join(
                            Environment.NewLine,
                            new[]
                            {
                                details,
                                $"Complete mip-chain decode failed: {chainException.Message}"
                            }.Where(value => !string.IsNullOrWhiteSpace(value))));
                }

                var available = new MaterialPreviewMipData(
                    0,
                    preview.Width,
                    preview.Height,
                    $"Available level · {preview.Width:N0} × {preview.Height:N0}",
                    preview.GetPngBytesCopy());
                return new MaterialPreviewLoadResult(
                    Array.AsReadOnly([available]),
                    $"{role} · {preview.Name} · available level only",
                    string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            details,
                            $"The complete mip chain is unavailable: {chainException.Message}"
                        }.Where(value => !string.IsNullOrWhiteSpace(value))));
            }
        }

        internal static MaterialPreviewLoadResult Failed(
            string message,
            string details) => new([], message, details);

        private static MaterialPreviewMipData CreatePreviewMip(
            ImageSourceMipLevel level,
            int mipLevel,
            bool isCube,
            bool isVolume)
        {
            if (level.Width <= 0 || level.Height <= 0 || level.Depth <= 0)
            {
                throw new InvalidDataException(
                    $"mip {mipLevel} has invalid dimensions " +
                    $"{level.Width}x{level.Height}x{level.Depth}");
            }

            int sliceByteCount = checked(level.Width * level.Height * 4);
            if (isCube)
            {
                const int faceCount = 6;
                const int columnCount = 3;
                const int rowCount = 2;
                byte[] sheet = TileLayers(
                    level,
                    faceCount,
                    columnCount,
                    rowCount,
                    sliceByteCount,
                    mipLevel);
                int sheetWidth = checked(level.Width * columnCount);
                int sheetHeight = checked(level.Height * rowCount);
                return new MaterialPreviewMipData(
                    mipLevel,
                    sheetWidth,
                    sheetHeight,
                    $"Mip {mipLevel:N0} · {level.Width:N0} × " +
                    $"{level.Height:N0} per face · 3 × 2 cube sheet " +
                    "(faces 0–5)",
                    PngWriter.WriteRgba(sheetWidth, sheetHeight, sheet));
            }

            if (isVolume)
            {
                int columnCount = checked((int)Math.Ceiling(
                    Math.Sqrt(level.Depth)));
                int rowCount = checked(
                    (level.Depth + columnCount - 1) / columnCount);
                byte[] sheet = TileLayers(
                    level,
                    level.Depth,
                    columnCount,
                    rowCount,
                    sliceByteCount,
                    mipLevel);
                int sheetWidth = checked(level.Width * columnCount);
                int sheetHeight = checked(level.Height * rowCount);
                return new MaterialPreviewMipData(
                    mipLevel,
                    sheetWidth,
                    sheetHeight,
                    $"Mip {mipLevel:N0} · {level.Width:N0} × " +
                    $"{level.Height:N0} × {level.Depth:N0} · " +
                    $"{columnCount:N0} × {rowCount:N0} slice sheet",
                    PngWriter.WriteRgba(sheetWidth, sheetHeight, sheet));
            }

            if (level.Depth != 1 ||
                level.RgbaBytes.Length != sliceByteCount)
            {
                throw new InvalidDataException(
                    $"mip {mipLevel} has an inconsistent 2D RGBA layout");
            }

            return new MaterialPreviewMipData(
                mipLevel,
                level.Width,
                level.Height,
                $"Mip {mipLevel:N0} · {level.Width:N0} × {level.Height:N0}",
                PngWriter.WriteRgba(
                    level.Width,
                    level.Height,
                    level.RgbaBytes.ToArray()));
        }

        private static byte[] TileLayers(
            ImageSourceMipLevel level,
            int layerCount,
            int columnCount,
            int rowCount,
            int layerByteCount,
            int mipLevel)
        {
            int expectedByteCount = checked(layerByteCount * layerCount);
            if (level.RgbaBytes.Length != expectedByteCount)
            {
                throw new InvalidDataException(
                    $"mip {mipLevel} has {level.RgbaBytes.Length:N0} RGBA " +
                    $"bytes; its {layerCount:N0} layers require " +
                    $"{expectedByteCount:N0}");
            }

            int sheetWidth = checked(level.Width * columnCount);
            int sheetHeight = checked(level.Height * rowCount);
            byte[] sheet = new byte[checked(sheetWidth * sheetHeight * 4)];
            int rowByteCount = checked(level.Width * 4);
            ReadOnlySpan<byte> source = level.RgbaBytes.Span;
            for (int layer = 0; layer < layerCount; layer++)
            {
                int destinationColumn = layer % columnCount;
                int destinationRow = layer / columnCount;
                for (int row = 0; row < level.Height; row++)
                {
                    int sourceOffset = checked(
                        layer * layerByteCount + row * rowByteCount);
                    int destinationOffset = checked(
                        ((destinationRow * level.Height + row) * sheetWidth +
                         destinationColumn * level.Width) * 4);
                    source.Slice(sourceOffset, rowByteCount).CopyTo(
                        sheet.AsSpan(destinationOffset, rowByteCount));
                }
            }

            return sheet;
        }

        private static int ComputeFullMipCount(
            int width,
            int height,
            int depth)
        {
            int count = 1;
            while (width > 1 || height > 1 || depth > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                depth = Math.Max(1, depth / 2);
                count++;
            }
            return count;
        }
    }

    private sealed record MaterialPreviewMipData(
        int Level,
        int Width,
        int Height,
        string Label,
        byte[] PngBytes);
}

public sealed class MaterialPreviewMipViewModel
{
    private readonly byte[] _pngBytes;

    internal MaterialPreviewMipViewModel(
        int level,
        int width,
        int height,
        string displayName,
        byte[] pngBytes)
    {
        Level = level;
        Width = width;
        Height = height;
        DisplayName = displayName;
        _pngBytes = pngBytes ?? throw new ArgumentNullException(nameof(pngBytes));
    }

    public int Level { get; }

    public int Width { get; }

    public int Height { get; }

    public string DisplayName { get; }

    internal byte[] PngBytes => _pngBytes;
}

internal sealed record MaterialImageImportTarget(
    MaterialDraft Draft,
    int TextureTableOrdinal);

internal sealed record MaterialImageExportTarget(
    GfxImageAsset Image,
    string SuggestedFileName);

internal sealed record MaterialImageExportPayload(
    byte[] Bytes,
    string SuggestedFileName);
