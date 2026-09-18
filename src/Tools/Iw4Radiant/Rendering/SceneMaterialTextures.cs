using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;
using PixelFormat = Avalonia.Platform.PixelFormat;

namespace Iw4Radiant.Rendering;

internal sealed class SceneMaterialTextures
{
    private const int FallbackSize = 128;
    private static readonly string[][] DefaultGlyphs =
    [
        ["11110", "10001", "10001", "10001", "10001", "10001", "11110"], // D
        ["11111", "10000", "10000", "11110", "10000", "10000", "11111"], // E
        ["11111", "10000", "10000", "11110", "10000", "10000", "10000"], // F
        ["01110", "10001", "10001", "11111", "10001", "10001", "10001"], // A
        ["10001", "10001", "10001", "10001", "10001", "10001", "01110"], // U
        ["10000", "10000", "10000", "10000", "10000", "10000", "11111"], // L
        ["11111", "00100", "00100", "00100", "00100", "00100", "00100"]  // T
    ];

    private readonly Dictionary<string, uint> _textures = new(StringComparer.Ordinal);
    private uint _fallbackTexture;

    internal string? Error { get; private set; }

    internal void Reload(GL gl)
    {
        Clear(gl);
        Error = null;
    }

    internal void RemoveUnused(GL gl, IEnumerable<string> materials)
    {
        var used = materials.ToHashSet(StringComparer.Ordinal);
        foreach (string material in _textures.Keys.Where(key => !used.Contains(key)).ToArray())
        {
            uint texture = _textures[material];
            if (texture != 0 && texture != _fallbackTexture)
                gl.DeleteTexture(texture);
            _textures.Remove(material);
        }
    }

    internal void ForgetHandles()
    {
        _textures.Clear();
        _fallbackTexture = 0;
    }

    internal unsafe uint GetTexture(GL gl, string material, Func<string, MaterialSource?>? resolveMaterial)
    {
        if (_textures.TryGetValue(material, out uint texture))
            return texture;
        texture = 0;
        try
        {
            MaterialSource? source = resolveMaterial?.Invoke(material);
            if (source is { IsSky: true })
            {
                // Sky materials require their cubemap path; never represent them as a 2D surface.
            }
            else if (source is null)
            {
                Error ??= $"Material '{material}' is unavailable. Surface shown with DEFAULT fallback.";
                texture = GetFallbackTexture(gl);
            }
            else
            {
                using var decoded = MaterialImages.Load(source, 1024);
                PixelSize size = decoded.PixelSize;
                using var converted = new WriteableBitmap(size, new Avalonia.Vector(96, 96),
                    PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                using var pixels = converted.Lock();
                decoded.CopyPixels(pixels);
                texture = gl.GenTexture();
                gl.BindTexture(TextureTarget.Texture2D, texture);
                gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
                gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                gl.PixelStore(PixelStoreParameter.UnpackRowLength, pixels.RowBytes / 4);
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)size.Width, (uint)size.Height,
                    0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels.Address.ToPointer());
                gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                gl.GenerateMipmap(TextureTarget.Texture2D);
            }
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception) || exception is UnauthorizedAccessException)
        {
            if (texture != 0)
                gl.DeleteTexture(texture);
            Error ??= $"Texture '{material}': {exception.Message} Surface shown with DEFAULT fallback.";
            texture = GetFallbackTexture(gl);
        }
        _textures.Add(material, texture);
        return texture;
    }

    internal void Clear(GL gl)
    {
        foreach (uint texture in _textures.Values)
            if (texture != 0 && texture != _fallbackTexture)
                gl.DeleteTexture(texture);
        _textures.Clear();
        if (_fallbackTexture != 0)
            gl.DeleteTexture(_fallbackTexture);
        _fallbackTexture = 0;
    }

    private unsafe uint GetFallbackTexture(GL gl)
    {
        if (_fallbackTexture != 0) return _fallbackTexture;

        uint texture = 0;
        try
        {
            byte[] pixels = CreateFallbackPixels();
            texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            fixed (byte* address = pixels)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, FallbackSize, FallbackSize, 0,
                    Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, address);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            _fallbackTexture = texture;
            return texture;
        }
        catch (Exception exception) when (SceneRenderer.IsRenderException(exception) || exception is UnauthorizedAccessException)
        {
            if (texture != 0) gl.DeleteTexture(texture);
            Error ??= $"DEFAULT fallback is unavailable: {exception.Message}";
            return 0;
        }
    }

    private static byte[] CreateFallbackPixels()
    {
        var pixels = new byte[FallbackSize * FallbackSize * 4];
        for (int y = 0; y < FallbackSize; y++)
        for (int x = 0; x < FallbackSize; x++)
        {
            byte shade = ((x / 16 + y / 16) & 1) == 0 ? (byte)112 : (byte)88;
            SetPixel(pixels, x, y, shade, shade, shade);
        }
        DrawDefault(pixels, 23, 24);
        DrawDefault(pixels, 23, 88);
        return pixels;
    }

    private static void DrawDefault(byte[] pixels, int left, int top)
    {
        const int scale = 2;
        for (int glyphIndex = 0; glyphIndex < DefaultGlyphs.Length; glyphIndex++)
        for (int row = 0; row < DefaultGlyphs[glyphIndex].Length; row++)
        for (int column = 0; column < DefaultGlyphs[glyphIndex][row].Length; column++)
        {
            if (DefaultGlyphs[glyphIndex][row][column] != '1') continue;
            for (int y = 0; y < scale; y++)
            for (int x = 0; x < scale; x++)
                SetPixel(pixels, left + (glyphIndex * 6 + column) * scale + x, top + row * scale + y, 245, 245, 245);
        }
    }

    private static void SetPixel(byte[] pixels, int x, int y, byte red, byte green, byte blue)
    {
        int offset = (y * FallbackSize + x) * 4;
        pixels[offset] = red;
        pixels[offset + 1] = green;
        pixels[offset + 2] = blue;
        pixels[offset + 3] = byte.MaxValue;
    }
}
