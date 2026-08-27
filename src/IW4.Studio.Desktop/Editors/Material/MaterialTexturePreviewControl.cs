using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using IW4.Render.OpenGl.MaterialPreview;
using IW4.Render.Resources;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Editors.Material;

/// <summary>
/// Avalonia OpenGL host and orbit input for the retained texture-shape
/// preview renderer.
/// </summary>
public sealed class MaterialTexturePreviewControl : OpenGlControlBase
{
    public static readonly StyledProperty<RenderTextureDescriptor?>
        TextureProperty = AvaloniaProperty.Register<
            MaterialTexturePreviewControl,
            RenderTextureDescriptor?>(nameof(Texture));

    public static readonly StyledProperty<int> SelectedMipLevelProperty =
        AvaloniaProperty.Register<MaterialTexturePreviewControl, int>(
            nameof(SelectedMipLevel));

    public static readonly StyledProperty<bool> UseSrgbReadsProperty =
        AvaloniaProperty.Register<MaterialTexturePreviewControl, bool>(
            nameof(UseSrgbReads));

    private SilkMaterialTexturePreviewRenderer? _renderer;
    private GL? _gl;
    private RenderTextureDescriptor? _uploadedTexture;
    private bool _uploadedUseSrgbReads;
    private bool _uploadRequired = true;
    private IPointer? _dragPointer;
    private Point _lastPointerPosition;
    private float _yaw = MathF.PI * 0.75f;
    private float _pitch = MathF.PI * 0.12f;
    private float _zoom = 1f;
    private string? _rendererFailure;
    private bool _hasSuccessfulUpload;

    public MaterialTexturePreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += Preview_PointerPressed;
        PointerMoved += Preview_PointerMoved;
        PointerReleased += Preview_PointerReleased;
        PointerCaptureLost += Preview_PointerCaptureLost;
        PointerWheelChanged += Preview_PointerWheelChanged;
        SizeChanged += (_, _) => RequestNextFrameRendering();
    }

    internal event EventHandler? RendererStatusChanged;

    internal string? RendererFailure => _rendererFailure;

    public RenderTextureDescriptor? Texture
    {
        get => GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
    }

    public int SelectedMipLevel
    {
        get => GetValue(SelectedMipLevelProperty);
        set => SetValue(SelectedMipLevelProperty, value);
    }

    public bool UseSrgbReads
    {
        get => GetValue(UseSrgbReadsProperty);
        set => SetValue(UseSrgbReadsProperty, value);
    }

    public void Fit()
    {
        _yaw = MathF.PI * 0.75f;
        _pitch = MathF.PI * 0.12f;
        _zoom = 1f;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            _renderer = new SilkMaterialTexturePreviewRenderer(_gl);
            _uploadRequired = true;
            PublishStatus(successfulUpload: false, failure: null);
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   ArgumentException or
                   NotSupportedException)
        {
            _renderer = null;
            _gl = null;
            PublishStatus(successfulUpload: false, exception.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
        _uploadedTexture = null;
        _uploadRequired = true;
        PublishStatus(successfulUpload: false, failure: null);
    }

    protected override void OnOpenGlLost()
    {
        _renderer = null;
        _gl = null;
        _uploadedTexture = null;
        _uploadRequired = true;
        PublishStatus(successfulUpload: false, failure: null);
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        if (_renderer is not { } renderer || Texture is not { } texture)
        {
            ClearTransparent(framebuffer, pixelSize);
            return;
        }

        if (_uploadRequired ||
            !ReferenceEquals(_uploadedTexture, texture) ||
            _uploadedUseSrgbReads != UseSrgbReads)
        {
            try
            {
                renderer.Upload(texture, UseSrgbReads);
                _uploadedTexture = texture;
                _uploadedUseSrgbReads = UseSrgbReads;
                _uploadRequired = false;
                PublishStatus(successfulUpload: true, failure: null);
            }
            catch (Exception exception) when (exception is
                       InvalidOperationException or
                       InvalidDataException or
                       ArgumentException or
                       NotSupportedException or
                       OverflowException)
            {
                _uploadedTexture = null;
                _uploadRequired = true;
                PublishStatus(successfulUpload: false, exception.Message);
                ClearTransparent(framebuffer, pixelSize);
                return;
            }
        }

        int selectedMip = Math.Clamp(
            SelectedMipLevel,
            0,
            texture.MipCount - 1);
        try
        {
            renderer.Render(
                framebuffer,
                pixelSize.Width,
                pixelSize.Height,
                selectedMip,
                _yaw,
                _pitch,
                _zoom);
            if (_rendererFailure is not null)
                PublishStatus(successfulUpload: true, failure: null);
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   InvalidDataException or
                   ArgumentException or
                   NotSupportedException or
                   OverflowException)
        {
            PublishStatus(successfulUpload: false, exception.Message);
            ClearTransparent(framebuffer, pixelSize);
        }
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextureProperty ||
            change.Property == UseSrgbReadsProperty)
        {
            _uploadedTexture = null;
            _uploadRequired = true;
            PublishStatus(successfulUpload: false, failure: null);
            Fit();
        }
        else if (change.Property == SelectedMipLevelProperty)
        {
            RequestNextFrameRendering();
        }
    }

    private void Preview_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (_dragPointer is not null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _dragPointer = e.Pointer;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void Preview_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        Point position = e.GetPosition(this);
        Vector delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        _yaw += (float)delta.X * 0.01f;
        _pitch = Math.Clamp(
            _pitch + (float)delta.Y * 0.01f,
            -MathF.PI * 0.48f,
            MathF.PI * 0.48f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void Preview_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        _dragPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Preview_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e) => _dragPointer = null;

    private void Preview_PointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        double wheel = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(wheel) || wheel == 0)
            return;

        _zoom = Math.Clamp(
            _zoom * MathF.Exp((float)wheel * 0.14f),
            0.35f,
            12f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void ClearTransparent(int framebuffer, PixelSize pixelSize)
    {
        if (_gl is not { } gl)
            return;
        gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            checked((uint)framebuffer));
        gl.DrawBuffer(framebuffer == 0
            ? DrawBufferMode.Back
            : DrawBufferMode.ColorAttachment0);
        gl.Viewport(
            0,
            0,
            checked((uint)pixelSize.Width),
            checked((uint)pixelSize.Height));
        gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    private void PublishStatus(bool successfulUpload, string? failure)
    {
        if (_hasSuccessfulUpload == successfulUpload &&
            string.Equals(_rendererFailure, failure, StringComparison.Ordinal))
        {
            return;
        }

        _hasSuccessfulUpload = successfulUpload;
        _rendererFailure = failure;
        RendererStatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
