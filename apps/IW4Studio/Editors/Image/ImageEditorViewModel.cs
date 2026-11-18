using Avalonia.Media.Imaging;
using IW4.Game.Assets.Image;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed record ImageTextureRole(TextureSemantic Value, string Label);

public sealed class ImageMipItem(ImageEditorMip mip, Bitmap thumbnail)
{
    internal ImageEditorMip Mip { get; } = mip;
    public Bitmap Thumbnail { get; } = thumbnail;
    public string Dimensions => $"{Mip.Width} × {Mip.Height}";
    public string Description => $"Mip {Mip.Level} · {Dimensions} px";
}

public sealed class ImageEditorViewModel : ObservableObject, IAssetEditorStagingState, IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly IGfxImagePayloadResolver _resolver;
    private ImageDraft _applied;
    private ImageDraft _draft;
    private CancellationTokenSource? _load;
    private IReadOnlyList<ImageMipItem> _mips = [];
    private ImageMipItem? _selectedMip;
    private Bitmap? _preview;
    private int _channelIndex;
    private int _zoomIndex;
    private bool _rendering;
    private bool _hasReplacement;
    private bool _busy;
    private bool _disposed;
    private string _status = string.Empty;
    private string _previewStatus = string.Empty;

    internal ImageEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _applied = session.OpenDraft<ImageDraft>();
        _draft = _applied.Clone();
        WorkspaceAssetProviderZone? provider = session.Entry.ResolvedProviderZone;
        WorkspaceZone? zone = session.Workspace.LoadedZones.FirstOrDefault(z =>
            session.CanEdit || provider?.IsTarget == true
                ? z.IsTarget
                : string.Equals(z.LogicalZoneName, provider?.LogicalZoneName, StringComparison.OrdinalIgnoreCase));
        _resolver = zone?.LoadResult.ImagePayloadResolver ?? UnavailableGfxImagePayloadResolver.Instance;
        Info = new ImageEditorInfo(_draft.Image);
        var roles = new List<ImageTextureRole>
        {
            new(TextureSemantic.TwoDimensional, "2D"),
            new(TextureSemantic.Function, "Function"),
            new(TextureSemantic.ColorMap, "Color map"),
            new(TextureSemantic.DetailMap, "Detail map"),
            new(TextureSemantic.NormalMap, "Normal map"),
            new(TextureSemantic.SpecularMap, "Specular map")
        };
        if (roles.All(role => role.Value != _draft.TextureSemantic))
            roles.Add(new(_draft.TextureSemantic, _draft.TextureSemantic.ToString()));
        TextureRoles = roles;
        ReloadPixels();
    }

    public string Name => _draft.Image.Name ?? "<unnamed image>";
    public ImageEditorInfo Info { get; private set; }
    public bool IsReadOnly => !_session.CanEdit || !_draft.CanEditMetadata;
    public bool CanEditMetadata => !_disposed && !_busy && _session.CanEdit && _draft.CanEditMetadata;
    public bool CanReplace => !_disposed && !_busy && _session.CanEdit && _draft.CanReplacePixels &&
        (_draft.Image.MapType is MapType.None or MapType.TwoDimensional) && !_draft.Image.IsCubemap;
    public bool CanExport => !_disposed && !_busy && FullResolutionMip is not null;
    public bool HasUnappliedChanges => _hasReplacement || _draft.TextureSemantic != _applied.TextureSemantic ||
        _draft.UsesSrgbReads != _applied.UsesSrgbReads;
    public bool CanApply => !_disposed && !_busy && _session.CanEdit && HasUnappliedChanges;
    public bool CanRevert => !_disposed && !_busy && _session.CanEdit && (HasUnappliedChanges || _session.HasUnsavedChanges);
    public string Status
    {
        get => !string.IsNullOrEmpty(_status) ? _status : HasUnappliedChanges ? "Unapplied changes" :
            _session.HasUnsavedChanges ? "Applied to the fastfile draft" : "No pending changes";
    }
    public string PreviewStatus
    {
        get => _previewStatus;
        private set
        {
            if (SetProperty(ref _previewStatus, value))
                OnPropertyChanged(nameof(HasPreviewNote));
        }
    }

    public IReadOnlyList<ImageTextureRole> TextureRoles { get; }
    public ImageTextureRole? SelectedRole
    {
        get => TextureRoles.FirstOrDefault(role => role.Value == _draft.TextureSemantic);
        set
        {
            if (!CanEditMetadata || value is null || value.Value == _draft.TextureSemantic)
                return;
            _draft.SetTextureSemantic(value.Value);
            ReportStatus(string.Empty);
            RefreshEditingState();
        }
    }

    public IReadOnlyList<string> ColorReadModes { get; } = ["Linear", "sRGB"];
    public int ColorReadIndex
    {
        get => _draft.UsesSrgbReads ? 1 : 0;
        set
        {
            if (!CanEditMetadata || value is < 0 or > 1 || value == ColorReadIndex)
                return;
            _draft.SetUsesSrgbReads(value == 1);
            ReportStatus(string.Empty);
            RefreshEditingState();
        }
    }
    public string ColorReadDescription => _draft.UsesSrgbReads
        ? "Decode color from sRGB to linear when sampled."
        : "Use the stored color values directly.";

    public IReadOnlyList<string> Channels { get; } = ["RGBA", "R", "G", "B", "A"];
    public int ChannelIndex
    {
        get => _channelIndex;
        set
        {
            if ((uint)value < (uint)Channels.Count && SetProperty(ref _channelIndex, value))
                _ = RenderSelectionAsync();
        }
    }

    public IReadOnlyList<string> ZoomOptions { get; } = ["Fit", "100%", "200%", "400%"];
    public int ZoomIndex
    {
        get => _zoomIndex;
        set
        {
            if ((uint)value < (uint)ZoomOptions.Count && SetProperty(ref _zoomIndex, value))
                OnPropertyChanged(nameof(Magnification));
        }
    }
    public double Magnification => ZoomIndex == 0 ? 0 : 1 << (ZoomIndex - 1);
    public IReadOnlyList<ImageMipItem> Mips => _mips;
    public bool HasMips => _mips.Count > 0;
    public ImageMipItem? SelectedMip
    {
        get => _selectedMip;
        set
        {
            if (value is not null && !_mips.Contains(value))
                return;
            if (SetProperty(ref _selectedMip, value))
                _ = RenderSelectionAsync();
        }
    }
    public Bitmap? Preview => _preview;
    public bool HasPreview => _preview is not null;
    public bool HasPreviewNote => HasPreview && !string.IsNullOrEmpty(PreviewStatus);
    internal ImageEditorMip? FullResolutionMip => _mips.FirstOrDefault(item => item.Mip.Level == 0)?.Mip;
    internal bool IsDisposed => _disposed;

    internal void ApplyChanges()
    {
        if (!CanApply)
            return;
        try
        {
            bool changed = _session.Apply<ImageDraft>(current => current.CopyFrom(_draft));
            ReadSessionDraft(reloadPixels: false);
            ReportStatus(changed ? "Applied to the fastfile draft. Use Save As to write the file." :
                "The Image already matches the applied draft.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportStatus($"Apply failed: {ex.Message}");
        }
    }

    internal void RevertChanges()
    {
        if (!CanRevert)
            return;
        try
        {
            bool staged = HasUnappliedChanges;
            if (!staged)
                _session.Revert();
            ReadSessionDraft(reloadPixels: _hasReplacement || !staged);
            ReportStatus(staged ? "Discarded staged changes." : "Restored the saved Image.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportStatus($"Revert failed: {ex.Message}");
        }
    }

    private void ReadSessionDraft(bool reloadPixels)
    {
        _applied = _session.OpenDraft<ImageDraft>();
        _draft = _applied.Clone();
        _hasReplacement = false;
        UpdateSource(reloadPixels);
    }

    internal Task<ImageReplacementReview> PrepareReplacementAsync(Stream stream, string fileName)
    {
        ImageDraft candidate = _draft.Clone();
        ImageEditorInfo current = Info;
        ImageEditorMip? before = _mips.FirstOrDefault()?.Mip;
        return Task.Run(() =>
        {
            var document = ImageEditorPixels.ReadPng(stream);
            candidate.ReplacePixels(document);
            var (levels, reason) = ImageEditorPixels.LoadAsset(candidate.Image,
                UnavailableGfxImagePayloadResolver.Instance, CancellationToken.None);
            ImageEditorMip replacement = levels.FirstOrDefault(level => level.Level == 0)
                ?? throw new InvalidDataException($"Could not preview the replacement. {reason}");
            return new ImageReplacementReview(fileName, candidate, current, before, replacement);
        });
    }

    internal void StageReplacement(ImageReplacementReview replacement)
    {
        if (_disposed)
            return;
        _draft.CopyFrom(replacement.Candidate);
        _hasReplacement = true;
        _load?.Cancel();
        ImageEditorMip level = replacement.Mip;
        SetMips([new ImageMipItem(level, ImageEditorPixels.CreateThumbnail(level, 48))]);
        PreviewStatus = string.Empty;
        UpdateSource(reloadPixels: false);
        ReportStatus($"Staged {replacement.FileName}. Apply changes to update the fastfile draft.");
    }

    private void UpdateSource(bool reloadPixels)
    {
        Info = new ImageEditorInfo(_draft.Image);
        OnPropertyChanged(nameof(Info));
        if (reloadPixels)
            ReloadPixels();
        RefreshEditingState();
    }

    internal void SetFileOperation(bool active)
    {
        _busy = active;
        RefreshEditingState();
    }

    internal void ReportStatus(string message)
    {
        _status = message;
        OnPropertyChanged(nameof(Status));
    }

    internal void RefreshSessionState()
    {
        _status = string.Empty;
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(Status));
    }

    private void RefreshEditingState()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanReplace));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanEditMetadata));
        OnPropertyChanged(nameof(SelectedRole));
        OnPropertyChanged(nameof(ColorReadIndex));
        OnPropertyChanged(nameof(ColorReadDescription));
        OnPropertyChanged(nameof(Status));
    }

    private void ReloadPixels()
    {
        _load?.Cancel();
        var loading = new CancellationTokenSource();
        _load = loading;
        SetMips([]);
        PreviewStatus = "Loading image pixels…";
        _ = LoadAsync(_draft.Image, loading);
    }

    private async Task LoadAsync(GfxImageAsset image, CancellationTokenSource loading)
    {
        var items = new List<ImageMipItem>();
        try
        {
            string status = await Task.Run(() =>
            {
                var (mips, description) = ImageEditorPixels.LoadAsset(image, _resolver, loading.Token);
                foreach (ImageEditorMip mip in mips)
                {
                    loading.Token.ThrowIfCancellationRequested();
                    items.Add(new ImageMipItem(mip, ImageEditorPixels.CreateThumbnail(mip, 48)));
                }
                return description;
            }, loading.Token);
            if (loading.IsCancellationRequested)
                return;
            SetMips(items.ToArray());
            items.Clear();
            PreviewStatus = status;
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!loading.IsCancellationRequested)
                PreviewStatus = $"Preview unavailable: {ex.Message}";
        }
        finally
        {
            foreach (var item in items)
                item.Thumbnail.Dispose();
            if (ReferenceEquals(_load, loading))
                _load = null;
            loading.Dispose();
        }
    }

    private void SetMips(IReadOnlyList<ImageMipItem> mips)
    {
        var old = _mips;
        _mips = mips;
        OnPropertyChanged(nameof(Mips));
        OnPropertyChanged(nameof(HasMips));
        SelectedMip = mips.FirstOrDefault();
        OnPropertyChanged(nameof(CanExport));
        foreach (ImageMipItem item in old)
            item.Thumbnail.Dispose();
        if (mips.Count == 0)
            SetPreview(null);
    }

    private async Task RenderSelectionAsync()
    {
        if (_rendering || _disposed || _selectedMip is null)
            return;
        _rendering = true;
        try
        {
            while (!_disposed && _selectedMip is { } selected)
            {
                int channel = _channelIndex;
                Bitmap bitmap = await Task.Run(() => ImageEditorPixels.CreatePreview(
                    selected.Mip, (ImagePreviewChannel)channel));
                if (!_disposed && ReferenceEquals(selected, _selectedMip) && channel == _channelIndex)
                {
                    SetPreview(bitmap);
                    return;
                }
                bitmap.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!_disposed)
            {
                SetPreview(null);
                PreviewStatus = $"Preview unavailable: {ex.Message}";
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void SetPreview(Bitmap? value)
    {
        Bitmap? old = _preview;
        _preview = value;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasPreviewNote));
        old?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        _load?.Cancel();
        SetMips([]);
    }
}
