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
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Render.Textures;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Workbench.Composition;
using IW4.Studio.Documents;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl;
using Silk.NET.OpenGL;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;

namespace IW4.Studio.Desktop.Editors.Fx;

/// <summary>Workspace FX audition using the same sampler and blend states as Radiant.</summary>
public sealed class FxSourcePreviewControl : UserControl, IDisposable
{
    private readonly FxSourceViewport _viewport = new();
    private readonly Button _pause = new() { Content = "Pause" };
    private readonly Button _replay = new() { Content = "Replay" };
    private readonly CheckBox _repeat = new() { Content = "Repeat", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Opacity = .7, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private int _revision;
    private bool _disposed;
    private int _statusQueued;

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
        _pause.Click += (_, _) => { if (_viewport.Preview is { } p) p.SetPaused(!p.IsPaused); _viewport.Refresh(); };
        _replay.Click += (_, _) => { if (_viewport.Preview is { } p) { p.Restart(); p.SetPaused(false); } _viewport.Refresh(); };
        _repeat.IsCheckedChanged += (_, _) => { if (_viewport.Preview is { } p) p.Repeat = _repeat.IsChecked == true; _viewport.Refresh(); };
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

    public async void SetEffect(FastFileWorkspace workspace, FxEffectDefAsset effect)
    {
        if (_disposed) return;
        int revision = ++_revision;
        string assetName = effect.Name ?? "";
        _status.Text = "Loading preview…";
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
            var pool = workspace.LoadedZone.Context.AssetPool;
            long poolRevision = pool.Revision;
            var result = await Task.Run(() =>
            {
                FxSpritePreview preview = FxSpritePreview.Load(effect, Vector3.Zero, Matrix4x4.Identity,
                    name => pool.TryResolve(XAssetType.Fx, name, out FxEffectDefAsset? child) && child is not null
                        ? child : throw new InvalidDataException($"Effect '{name}' is not loaded."),
                    name => pool.TryResolve(XAssetType.XModel, name, out XModelAsset? model) && model is not null
                        ? FxModelPreviewGeometry.FromModel(model)
                        : throw new InvalidDataException($"Model '{name}' is not loaded."));
                preview.SetPaused(true);
                var materials = new Dictionary<string, FxPreviewMaterial>(StringComparer.Ordinal);
                var notices = new List<string>();
                if (!string.IsNullOrEmpty(preview.Notice)) notices.Add(preview.Notice);
                foreach (string name in preview.Materials)
                {
                    try
                    {
                        FxPreviewMaterial? material = FxPreviewMaterial.Load(workspace, name);
                        if (material is null) notices.Add("Heat distortion is omitted from this preview.");
                        else materials.Add(name, material);
                    }
                    catch (Exception ex) when (IsSourceError(ex)) { notices.Add($"{name}: {ex.Message}"); }
                }
                if (pool.Revision != poolRevision)
                    throw new InvalidOperationException("Assets changed while loading the preview. Reopen the editor to refresh.");
                return (preview, materials, notice: string.Join("; ", notices));
            });
            if (_disposed || revision != _revision) return;
            if (pool.Revision != poolRevision)
                throw new InvalidOperationException("Assets changed while loading the preview. Reopen the editor to refresh.");
            _repeat.IsChecked = false;
            _viewport.SetPreview(result.preview, result.materials, result.notice, assetName);
            result.preview.Restart(); result.preview.SetPaused(false);
            UpdateControls();
        }
        catch (Exception ex) when (IsSourceError(ex))
        {
            if (_disposed || revision != _revision) return;
            _viewport.SetPreview(null, new Dictionary<string, FxPreviewMaterial>(), ex.Message, assetName);
            UpdateControls();
        }
    }

    internal static Task<Bitmap?> LoadMaterialBitmapAsync(FastFileWorkspace workspace, string materialName) =>
        Task.Run(() =>
        {
            FxPreviewMaterial? material = FxPreviewMaterial.Load(workspace, materialName);
            if (material is null) return null;
            unsafe
            {
                fixed (byte* pixels = material.Pixels)
                    return new Bitmap(Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Unpremul,
                        (nint)pixels, new PixelSize(material.Width, material.Height), new Avalonia.Vector(96, 96),
                        checked(material.Width * 4));
            }
        });

    public void Clear() { ++_revision; _viewport.SetPreview(null, new Dictionary<string, FxPreviewMaterial>(), "", ""); UpdateControls(); }
    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); }
    private void UpdateControls()
    {
        FxSpritePreview? p = _viewport.Preview;
        _pause.IsEnabled = p is { HasDrawableElements: true, IsFinished: false };
        _pause.Content = p?.IsPaused == true ? "Resume" : "Pause";
        _replay.IsEnabled = p is { HasDrawableElements: true };
        _replay.Content = p?.IsLooping == true ? "Restart" : "Replay";
        _repeat.IsVisible = p is { IsLooping: false };
        _status.Text = p is null ? "Preview unavailable" : !p.HasDrawableElements ? "No supported visual layers" :
            p.IsFinished ? "Finished · Replay to watch again" : p.IsPaused ? "Paused" : "Right-drag to orbit · scroll to zoom";
        if (_viewport.Notice.Length > 0) _status.Text += " · Limited preview";
        ToolTip.SetTip(_status, _viewport.Notice.Length > 0 ? _viewport.Notice : _status.Text);
    }

    private static bool IsSourceError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or NotSupportedException or InvalidOperationException or OverflowException or
        KeyNotFoundException or FormatException;

    private sealed record FxPreviewMaterial(MaterialSurfaceState Surface, int Width, int Height, byte[] Pixels)
    {
        internal static FxPreviewMaterial? Load(FastFileWorkspace workspace, string name)
        {
            var pool = workspace.LoadedZone.Context.AssetPool;
            if (!pool.TryResolve(XAssetType.Material, name, out MaterialAsset? material) || material is null)
                throw new InvalidDataException($"Material '{name}' is not loaded.");
            // Use the canonical source projection so both editors interpret the same native state.
            using var json = JsonDocument.Parse(new MaterialExchange().ToJson(material));
            MaterialSurfaceState surface = MaterialSurfaceState.Read(json.RootElement);
            if (surface.SortKey == (int)MaterialSortKey.Distortion) return null;
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
            return new(surface, decoded.Width, decoded.Height,
                SourceImageDumpDecoder.ApplyComponentMapping(image, decoded.GetRgbaBytesCopy()));
        }
    }

    private sealed class FxSourceViewport : OpenGlControlBase
    {
        private GL? _gl;
        private uint _program, _vao, _buffer;
        private readonly Dictionary<string, uint> _textures = new(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, FxPreviewMaterial> _materials = new Dictionary<string, FxPreviewMaterial>();
        private bool _reloadTextures;
        private string _assetName = "";
        private float _yaw = -.9f, _pitch = .2f, _distance = 256;
        private Vector3 _target;
        private Point _last;
        private IPointer? _pointer;
        internal FxSpritePreview? Preview { get; private set; }
        internal string Notice { get; private set; } = "";
        internal event Action? StateChanged;

        internal FxSourceViewport()
        {
            Focusable = true;
            SizeChanged += (_, _) => Refresh();
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
                Point p = e.GetPosition(this); _yaw -= (float)(p.X - _last.X) * .008f;
                _pitch = Math.Clamp(_pitch + (float)(p.Y - _last.Y) * .008f, -1.5f, 1.5f);
                _last = p; Refresh(); e.Handled = true;
            };
            surface.PointerReleased += (_, e) => { if (_pointer == e.Pointer) { _pointer = null; e.Pointer.Capture(null); } };
            surface.PointerCaptureLost += (_, _) => _pointer = null;
            surface.PointerWheelChanged += (_, e) => { _distance = Math.Clamp(_distance * MathF.Exp((float)-e.Delta.Y * .12f), .1f, 1_000_000); Refresh(); e.Handled = true; };
        }

        internal void SetPreview(FxSpritePreview? preview, IReadOnlyDictionary<string, FxPreviewMaterial> materials,
            string notice, string assetName)
        {
            Preview?.SetPaused(true);
            bool frame = _assetName != assetName || Preview is null;
            _assetName = assetName; Preview = preview; _materials = materials; Notice = notice;
            _reloadTextures = true;
            if (frame) Frame();
            Refresh();
        }
        internal void Refresh() { RequestNextFrameRendering(); StateChanged?.Invoke(); }
        internal void Frame()
        {
            if (Preview is { } p)
            {
                _target = (p.PreviewBounds.Min + p.PreviewBounds.Max) / 2;
                _distance = Math.Max(16, Vector3.Distance(p.PreviewBounds.Min, p.PreviewBounds.Max) * 1.5f);
            }
            _yaw = -.9f; _pitch = .2f; Refresh();
        }

        protected override unsafe void OnOpenGlInit(GlInterface glInterface)
        {
            _gl = GL.GetApi(glInterface.GetProcAddress);
            try
            {
                string header = _gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.Ordinal)
                    ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";
                _program = CreateProgram(_gl, header);
                _vao = _gl.GenVertexArray(); _buffer = _gl.GenBuffer();
                _gl.BindVertexArray(_vao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
                uint stride = (uint)sizeof(FxPreviewVertex);
                _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
                _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)24);
                _gl.EnableVertexAttribArray(2); _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)32);
                _gl.BindVertexArray(0); _reloadTextures = true;
            }
            catch (Exception ex) when (IsSourceError(ex)) { Notice = ex.Message; Release(); StateChanged?.Invoke(); }
        }
        protected override void OnOpenGlDeinit(GlInterface gl) => Release();
        protected override void OnOpenGlLost()
        {
            _textures.Clear(); _program = _vao = _buffer = 0; _gl = null; _reloadTextures = true;
        }
        private void Release()
        {
            if (_gl is { } gl)
            {
                foreach (uint texture in _textures.Values) gl.DeleteTexture(texture);
                if (_buffer != 0) gl.DeleteBuffer(_buffer);
                if (_vao != 0) gl.DeleteVertexArray(_vao);
                if (_program != 0) gl.DeleteProgram(_program);
                gl.Dispose();
            }
            _textures.Clear(); _gl = null; _program = _vao = _buffer = 0;
        }
        protected override unsafe void OnOpenGlRender(GlInterface glInterface, int framebuffer)
        {
            if (_gl is not { } gl || _program == 0) return;
            try
            {
                if (_reloadTextures)
                {
                    foreach (uint texture in _textures.Values) gl.DeleteTexture(texture);
                    _textures.Clear(); _reloadTextures = false;
                }
                float scaling = (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
                uint width = (uint)Math.Max(1, Bounds.Width * scaling), height = (uint)Math.Max(1, Bounds.Height * scaling);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
                gl.Viewport(0, 0, width, height); gl.DepthMask(true);
                gl.ClearColor(.07f, .08f, .095f, 1); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                if (Preview is not { } preview) return;
                Vector3 eye = _target + _distance * new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
                Matrix4x4 vp = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitZ) *
                    Matrix4x4.CreatePerspectiveFieldOfView(.85f, (float)width / height, Math.Max(.01f, _distance / 10_000), Math.Max(4096, _distance * 20));
                gl.UseProgram(_program); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
                gl.UniformMatrix4(gl.GetUniformLocation(_program, "uViewProjection"), 1, false, (float*)&vp);
                gl.ActiveTexture(TextureUnit.Texture0); gl.Uniform1(gl.GetUniformLocation(_program, "uTexture"), 0);
                int alpha = gl.GetUniformLocation(_program, "uAlphaTest"), premultiply = gl.GetUniformLocation(_program, "uPremultiplyAlpha");
                foreach (var (name, vertices) in preview.Sample(eye))
                {
                    if (!_materials.TryGetValue(name, out var material)) continue;
                    if (!_textures.TryGetValue(name, out uint texture))
                    {
                        texture = gl.GenTexture(); _textures.Add(name, texture);
                        gl.BindTexture(TextureTarget.Texture2D, texture); gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
                        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1); gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                        fixed (byte* data = material.Pixels)
                            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)material.Width, (uint)material.Height,
                                0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, data);
                        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                        gl.GenerateMipmap(TextureTarget.Texture2D);
                    }
                    MaterialPreviewDrawing.Apply(gl, material.Surface, alpha, premultiply, -1);
                    gl.Disable(EnableCap.CullFace); gl.BindTexture(TextureTarget.Texture2D, texture);
                    fixed (FxPreviewVertex* data = vertices)
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(FxPreviewVertex)), data, BufferUsageARB.DynamicDraw);
                    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
                }
                gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.PolygonOffsetFill);
                gl.BindVertexArray(0); gl.UseProgram(0);
                StateChanged?.Invoke();
                if (preview.IsPlaying) RequestNextFrameRendering();
            }
            catch (Exception ex) when (IsSourceError(ex)) { Preview?.SetPaused(true); Notice = ex.Message; StateChanged?.Invoke(); }
        }

        private static uint CreateProgram(GL gl, string header)
        {
            const string vertex = "in vec3 aPosition; in vec2 aUv; in vec4 aColor; uniform mat4 uViewProjection; out vec2 uv; out vec4 color; void main(){ uv=aUv; color=aColor; gl_Position=uViewProjection*vec4(aPosition,1.0); }";
            const string fragment = "in vec2 uv; in vec4 color; uniform sampler2D uTexture; uniform int uAlphaTest; uniform bool uPremultiplyAlpha; out vec4 fragmentColor; void main(){ vec4 c=texture(uTexture,uv)*color; if((uAlphaTest==1 && c.a<=0.0)||(uAlphaTest==2 && c.a>=128.0/255.0)||(uAlphaTest==3 && c.a<128.0/255.0)) discard; fragmentColor=vec4(uPremultiplyAlpha ? c.rgb*c.a : c.rgb,c.a); }";
            uint v = 0, f = 0, program = 0;
            try
            {
                v = Compile(ShaderType.VertexShader, vertex); f = Compile(ShaderType.FragmentShader, fragment);
                program = gl.CreateProgram(); gl.AttachShader(program, v); gl.AttachShader(program, f);
                gl.BindAttribLocation(program, 0, "aPosition"); gl.BindAttribLocation(program, 1, "aUv"); gl.BindAttribLocation(program, 2, "aColor");
                gl.LinkProgram(program); gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
                if (linked == 0) throw new InvalidOperationException(gl.GetProgramInfoLog(program));
                return program;
            }
            catch { if (program != 0) gl.DeleteProgram(program); throw; }
            finally { if (v != 0) gl.DeleteShader(v); if (f != 0) gl.DeleteShader(f); }
            uint Compile(ShaderType type, string source)
            {
                uint shader = gl.CreateShader(type); gl.ShaderSource(shader, header + source); gl.CompileShader(shader);
                gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
                if (compiled != 0) return shader;
                string error = gl.GetShaderInfoLog(shader); gl.DeleteShader(shader); throw new InvalidOperationException(error);
            }
        }
    }
}
