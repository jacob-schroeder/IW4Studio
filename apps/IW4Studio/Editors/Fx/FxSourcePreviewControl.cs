using System.Numerics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Render.SceneBuilding;
using IW4.Render.Textures;
using IW4.Render.Transforms;
using IW4.Studio.Desktop.Persistence;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;
using IW4.Render.EditorPreview;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Editors.Fx;

/// <summary>Plays sampled FX geometry with material passes from the active workspace.</summary>
public sealed class FxSourcePreviewControl : UserControl, IDisposable
{
    private readonly FxSourceViewport _viewport = new();
    private readonly Button _pause = new() { Content = "Pause" };
    private readonly Button _replay = new() { Content = "Replay" };
    private readonly CheckBox _repeat = new() { Content = "Repeat", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Opacity = .7, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private int _revision;
    private bool _disposed;
    private bool _loading;
    private CancellationTokenSource? _loadCancellation;
    private int _statusQueued;
    private bool _showEditorTabHints;

    public bool ShowEditorTabHints
    {
        get => _showEditorTabHints;
        set
        {
            if (_showEditorTabHints == value) return;
            _showEditorTabHints = value;
            UpdateControls();
        }
    }

    public FxSourcePreviewControl()
    {
        var frame = new Button { Content = "Reset view" };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(10), Children = { _pause, _replay, _repeat, frame } };
        var footer = new Border { Padding = new Thickness(12, 8), Child = _status };
        var layout = new DockPanel();
        DockPanel.SetDock(controls, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        var inputSurface = new Border { Background = Avalonia.Media.Brushes.Transparent, Focusable = true };
        var scene = new Grid { Children = { _viewport, inputSurface } };
        _viewport.BindInput(inputSurface);
        layout.Children.Add(controls); layout.Children.Add(footer); layout.Children.Add(scene);
        Content = layout;
        _pause.Click += (_, _) => { if (_viewport.Preview is { } p) p.SetPaused(!p.IsPaused); _viewport.Refresh(); UpdateControls(); };
        _replay.Click += (_, _) => { if (_viewport.Preview is { } p) { p.Restart(); p.SetPaused(false); } _viewport.Refresh(); UpdateControls(); };
        _repeat.IsCheckedChanged += (_, _) => { if (_viewport.Preview is { } p) p.Repeat = _repeat.IsChecked == true; _viewport.Refresh(); UpdateControls(); };
        frame.Click += (_, _) => _viewport.Frame();
        _viewport.StateChanged += () =>
        {
            if (Interlocked.Exchange(ref _statusQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _statusQueued, 0);
                if (!_disposed) UpdateControls();
            });
        };
        UpdateControls();
    }

    public async Task SetEffectAsync(
        FastFileWorkspace workspace,
        FxEffectDefAsset effect,
        bool repeatOneShots = false,
        int? selectedElementIndex = null)
    {
        if (_disposed) return;
        int revision = ++_revision;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        string assetName = effect.Name ?? "";
        _loading = true;
        _viewport.SetPreview(null, null, "", assetName);
        UpdateControls();
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
            var pool = workspace.LoadedZone.Context.AssetPool;
            long poolRevision = pool.Revision;
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                FxSpritePreview preview = FxSpritePreview.Load(effect, Vector3.Zero, Matrix4x4.Identity,
                    name => pool.TryResolve(XAssetType.Fx, name, out FxEffectDefAsset? child) && child is not null
                        ? child : throw new InvalidDataException($"Effect '{name}' is not loaded."),
                    name => pool.TryResolve(XAssetType.XModel, name, out XModelAsset? model) && model is not null
                        ? FxModelPreviewGeometry.FromModel(model)
                        : throw new InvalidDataException($"Model '{name}' is not loaded."),
                    selectedElementIndex);
                preview.SetPaused(true);
                XModelRenderScene? scene = preview.HasDrawableElements
                    ? new FxMaterialSceneBuilder().Build(
                        preview.Materials,
                        WorkspaceRenderAssetSource.Create(workspace, "FX playback materials"),
                        new WorkspaceGfxImagePayloadResolver(workspace), token)
                    : null;
                token.ThrowIfCancellationRequested();
                return (preview, scene);
            }, token);
            if (_disposed || revision != _revision) return;
            if (pool.Revision != poolRevision)
                throw new InvalidOperationException("Assets changed while loading the preview. Reopen the editor to refresh.");
            _loading = false;
            result.preview.Repeat = repeatOneShots;
            _viewport.SetPreview(result.preview, result.scene, result.preview.Notice ?? "", assetName, selectedElementIndex);
            _repeat.IsChecked = repeatOneShots;
            UpdateControls();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (IsSourceError(ex))
        {
            if (_disposed || revision != _revision) return;
            _loading = false;
            _viewport.SetPreview(null, null, ex.Message, assetName);
            UpdateControls();
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    internal static Task<Bitmap?> LoadMaterialBitmapAsync(FastFileWorkspace workspace, string materialName) =>
        Task.Run(() =>
        {
            var pool = workspace.LoadedZone.Context.AssetPool;
            if (!pool.TryResolve(XAssetType.Material, materialName, out MaterialAsset? material) || material is null)
                throw new InvalidDataException($"Material '{materialName}' is not loaded.");
            if (material.Info.SortKey == MaterialSortKey.Distortion) return null;
            using var json = JsonDocument.Parse(new MaterialExchange().ToJson(material));
            JsonElement texture = json.RootElement.GetProperty("textures").EnumerateArray().FirstOrDefault(t =>
                t.TryGetProperty("name", out var n) && n.GetString() == "colorMap");
            if (texture.ValueKind != JsonValueKind.Object || !texture.TryGetProperty("image", out var imageName) ||
                imageName.GetString() is not { Length: > 0 } imageKey)
                throw new InvalidDataException("No color-map image.");
            GfxImageAsset? image = pool.TryResolve(XAssetType.Image, imageKey, out GfxImageAsset? current) && current is not null
                ? current : material.Textures.Select(t => t.Image).FirstOrDefault(i => i?.Name?.TrimStart(',') == imageKey);
            if (image is null) throw new InvalidDataException($"Image '{imageKey}' is not loaded.");
            if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(image, new WorkspaceGfxImagePayloadResolver(workspace),
                out GfxImagePreviewSnapshot? decoded, out string reason) || decoded is null)
                throw new InvalidDataException(reason);
            byte[] rgba = decoded.GetRgbaBytesCopy();
            RsxTextureSwizzleDecoder.Decode(RsxTextureCommandBuilder.FromImage(image)).ApplyToRgba(rgba);
            unsafe
            {
                fixed (byte* pixels = rgba)
                    return new Bitmap(Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Unpremul,
                        (nint)pixels, new PixelSize(decoded.Width, decoded.Height), new Avalonia.Vector(96, 96),
                        checked(decoded.Width * 4));
            }
        });

    public void Clear()
    {
        ++_revision;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _loading = false;
        _viewport.SetPreview(null, null, "", "");
        UpdateControls();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); }
    private void UpdateControls()
    {
        FxSpritePreview? p = _viewport.Preview;
        bool canPlay = _viewport.Failure.Length == 0 && p is { HasDrawableElements: true } &&
            _viewport.MaterialCount > 0;
        bool pauseEnabled = canPlay && p is { IsFinished: false };
        if (_pause.IsEnabled != pauseEnabled) _pause.IsEnabled = pauseEnabled;
        string pauseLabel = p?.IsPaused == true ? "Resume" : "Pause";
        if (!Equals(_pause.Content, pauseLabel)) _pause.Content = pauseLabel;
        if (_replay.IsEnabled != canPlay) _replay.IsEnabled = canPlay;
        string replayLabel = p?.IsLooping == true ? "Restart" : "Replay";
        if (!Equals(_replay.Content, replayLabel)) _replay.Content = replayLabel;
        bool repeatVisible = p is { IsLooping: false };
        if (_repeat.IsVisible != repeatVisible) _repeat.IsVisible = repeatVisible;
        string status;
        if (_loading || _viewport.PreparingMaterials) status = "Preparing FX materials…";
        else if (_viewport.Failure.Length > 0) status = $"Preview unavailable: {_viewport.Failure}";
        else if (p is null) status = _viewport.Notice.Length > 0 ? _viewport.Notice : "Preview unavailable";
        else if (!p.HasDrawableElements) status = NoDrawableStatus(p.Notice);
        else if (_viewport.MaterialCount == 0) status = "No material pass could draw this effect.";
        else
        {
            status = p.IsFinished ? "Finished · Replay to watch again" :
                p.IsPaused ? "Paused" : "Right-drag to orbit · scroll to zoom";
            if (_viewport.BlockedMaterials > 0)
                status += $" · {_viewport.MaterialCount} of {p.Materials.Count} materials ready";
            if (_viewport.FrameIssues.Count > 0)
                status += " · Some particles could not draw";
            if (_viewport.Notice.Length > 0)
                status += " · " + _viewport.Notice.Split(';', 2)[0];
        }
        if (_status.Text != status) _status.Text = status;
        string tip = string.Join("\n", new[] { _viewport.Failure, _viewport.MaterialDetails, _viewport.Notice }
            .Concat(_viewport.FrameIssues).Where(message => message.Length > 0));
        if (tip.Length == 0) tip = status;
        if (!Equals(ToolTip.GetTip(_status), tip)) ToolTip.SetTip(_status, tip);
    }

    private string NoDrawableStatus(string? notice)
    {
        if (notice?.Contains("Sound elements omitted", StringComparison.Ordinal) == true)
            return "Sound has no canvas preview" + (_showEditorTabHints ? " · See Visual / Audio" : "");
        if (!string.IsNullOrWhiteSpace(notice))
            return $"No drawable layers: {notice.Split(';')[0].Trim()}";
        return "No drawable layers";
    }

    internal static bool IsSourceError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or NotSupportedException or InvalidOperationException or OverflowException or
        KeyNotFoundException or FormatException;

    private sealed class FxSourceViewport : OpenGlControlBase
    {
        private GL? _gl;
        private SilkXModelViewerRenderer? _renderer;
        private XModelRenderScene? _scene;
        private bool _uploadRequired;
        private bool _startPlaybackAfterUpload;
        private Visual[] _ancestors = [];
        private bool _attached;
        private const float VerticalFieldOfView = .85f;
        private const float FrameFill = .82f;
        private string _assetName = "";
        private int? _framedLayer;
        private bool _autoFrame = true;
        private float _yaw = -.9f, _pitch = .2f, _distance = 256;
        private Vector3 _target;
        private Point _last;
        private IPointer? _pointer;
        internal FxSpritePreview? Preview { get; private set; }
        internal string Notice { get; private set; } = "";
        internal string Failure { get; private set; } = "";
        internal int MaterialCount { get; private set; }
        internal int BlockedMaterials { get; private set; }
        internal string MaterialDetails { get; private set; } = "";
        internal IReadOnlyList<string> FrameIssues { get; private set; } = [];
        internal bool PreparingMaterials => _scene is not null && _uploadRequired && Failure.Length == 0;
        internal event Action? StateChanged;

        internal FxSourceViewport()
        {
            Focusable = true;
            SizeChanged += (_, _) =>
            {
                if (_autoFrame) Frame();
                else Refresh();
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _ancestors = this.GetVisualAncestors().Prepend(this).ToArray();
            foreach (Visual visual in _ancestors) visual.PropertyChanged += VisibilityChanged;
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            foreach (Visual visual in _ancestors) visual.PropertyChanged -= VisibilityChanged;
            _ancestors = [];
            base.OnDetachedFromVisualTree(e);
        }

        private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && _ancestors.All(visual => visual.IsVisible))
                RequestNextFrameRendering();
        }

        internal void BindInput(Control surface)
        {
            surface.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
                surface.Focus();
                _pointer = e.Pointer; _last = e.GetPosition(this); e.Pointer.Capture(surface); e.Handled = true;
            };
            surface.PointerMoved += (_, e) =>
            {
                if (_pointer != e.Pointer) return;
                _autoFrame = false;
                Point p = e.GetPosition(this); _yaw -= (float)(p.X - _last.X) * .008f;
                _pitch = Math.Clamp(_pitch + (float)(p.Y - _last.Y) * .008f, -1.5f, 1.5f);
                _last = p; Refresh(); e.Handled = true;
            };
            surface.PointerReleased += (_, e) => { if (_pointer == e.Pointer) { _pointer = null; e.Pointer.Capture(null); } };
            surface.PointerCaptureLost += (_, _) => _pointer = null;
            surface.PointerWheelChanged += (_, e) =>
            {
                _autoFrame = false;
                _distance = Math.Clamp(_distance * MathF.Exp((float)-e.Delta.Y * .12f), .1f, 1_000_000);
                Refresh();
                e.Handled = true;
            };
        }

        internal void SetPreview(FxSpritePreview? preview, XModelRenderScene? scene,
            string notice, string assetName, int? selectedLayer = null)
        {
            Preview?.SetPaused(true);
            bool frame = preview is not null && (_autoFrame || _assetName != assetName || _framedLayer != selectedLayer);
            if (preview is not null || assetName.Length == 0)
            {
                _assetName = assetName;
                _framedLayer = selectedLayer;
            }
            Preview = preview;
            _scene = scene;
            Notice = notice;
            MaterialCount = BlockedMaterials = 0;
            MaterialDetails = "";
            FrameIssues = [];
            if (_renderer is not null) Failure = "";
            _uploadRequired = true;
            _startPlaybackAfterUpload = preview is not null;
            if (frame) Frame();
            else Refresh();
        }
        internal void Refresh()
        {
            if (_attached && IsEffectivelyVisible)
                RequestNextFrameRendering();
        }
        private Vector3 EyeDirection => new(
            MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));

        internal void Frame()
        {
            _autoFrame = true;
            _yaw = -.9f;
            _pitch = .2f;
            if (Preview is { } preview && Bounds.Width > 0 && Bounds.Height > 0)
            {
                var focus = preview.FocusBounds;
                _target = focus.Center;
                var half = (focus.Max - focus.Min) * .5f;
                Vector3 towardEye = EyeDirection;
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, towardEye));
                Vector3 up = Vector3.Cross(towardEye, right);
                float halfHeight = MathF.Tan(VerticalFieldOfView / 2) * FrameFill;
                float halfWidth = halfHeight * (float)(Bounds.Width / Bounds.Height);

                // Fit each corner in perspective, including its distance toward the camera.
                _distance = .1f;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new((corner & 1) == 0 ? -half.X : half.X,
                        (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
                    float fitX = MathF.Abs(Vector3.Dot(offset, right)) / halfWidth;
                    float fitY = MathF.Abs(Vector3.Dot(offset, up)) / halfHeight;
                    _distance = MathF.Max(_distance, Vector3.Dot(offset, towardEye) + MathF.Max(fitX, fitY));
                }
            }
            Refresh();
        }

        protected override void OnOpenGlInit(GlInterface gl)
        {
            try
            {
                _gl = GL.GetApi(gl.GetProcAddress);
                _renderer = new SilkXModelViewerRenderer(_gl, OpenGlProgramBinaryCachePath.GetDirectory());
                _uploadRequired = true;
                Failure = "";
            }
            catch (Exception error) when (IsSourceError(error))
            {
                Failure = error.Message;
            }
            StateChanged?.Invoke();
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _renderer?.Dispose();
            ReleaseContext();
        }

        protected override void OnOpenGlLost() => ReleaseContext();

        private void ReleaseContext()
        {
            _renderer = null;
            _gl?.Dispose();
            _gl = null;
            _uploadRequired = true;
        }

        protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
        {
            if (_renderer is not { } renderer || Failure.Length > 0) return;
            try
            {
                if (_uploadRequired)
                {
                    _uploadRequired = false;
                    var prepared = renderer.UploadFxMaterials(_scene);
                    MaterialCount = prepared.ExecutableGroupCount;
                    BlockedMaterials = prepared.BlockedGroupCount;
                    MaterialDetails = string.Join("\n", prepared.Diagnostics);
                    if (_startPlaybackAfterUpload && MaterialCount > 0 && Preview is { } ready)
                    {
                        ready.Restart();
                        ready.SetPaused(false);
                        _startPlaybackAfterUpload = false;
                    }
                    StateChanged?.Invoke();
                }

                double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
                int width = Math.Max(1, (int)(Bounds.Width * scale));
                int height = Math.Max(1, (int)(Bounds.Height * scale));
                Vector3 eye = _target + _distance * EyeDirection;
                Vector3 forward = RenderCoordinateConverter.GameToRenderPosition(-EyeDirection);
                var camera = new RenderCamera(RenderCoordinateConverter.GameToRenderPosition(eye),
                    MathF.Atan2(forward.X, -forward.Z), MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)),
                    VerticalFieldOfView, MathF.Max(.01f, _distance / 10_000), MathF.Max(4096, _distance * 20));
                float materialTime = 0;
                IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> batches =
                    MaterialCount > 0 && Preview is { } active ? active.Sample(eye, out materialTime) : [];
                var issues = renderer.RenderFx(framebuffer, width, height, camera, materialTime, batches);
                if (batches.Count > 0 && !FrameIssues.SequenceEqual(issues))
                {
                    FrameIssues = issues;
                    StateChanged?.Invoke();
                }
                if (MaterialCount > 0 && Preview is { } preview)
                {
                    if (preview.IsPlaying && _attached && IsEffectivelyVisible)
                        RequestNextFrameRendering();
                    else if (preview.IsFinished)
                        StateChanged?.Invoke();
                }
            }
            catch (Exception error) when (IsSourceError(error))
            {
                Preview?.SetPaused(true);
                Failure = error.Message;
                StateChanged?.Invoke();
            }
        }
    }
}
