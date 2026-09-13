using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Iw4Radiant.Materials;
using Silk.NET.OpenGL;
using PixelFormat = Avalonia.Platform.PixelFormat;

namespace Iw4Radiant.Rendering;

internal sealed class SceneMaterialTextures
{
    private readonly Dictionary<string, uint> _textures = new(StringComparer.Ordinal);

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
            if (_textures[material] != 0)
                gl.DeleteTexture(_textures[material]);
            _textures.Remove(material);
        }
    }

    internal void ForgetHandles() => _textures.Clear();

    internal unsafe uint GetTexture(GL gl, string material, Func<string, string?>? resolveTexturePath)
    {
        if (_textures.TryGetValue(material, out uint texture))
            return texture;
        texture = 0;
        try
        {
            string? path = resolveTexturePath?.Invoke(material);
            if (path is not null)
            {
                using var decoded = MaterialImages.Load(path, 1024);
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
            texture = 0;
            Error ??= $"Texture '{material}': {exception.Message} Surface shown as wireframe.";
        }
        _textures.Add(material, texture);
        return texture;
    }

    internal void Clear(GL gl)
    {
        foreach (uint texture in _textures.Values)
            if (texture != 0)
                gl.DeleteTexture(texture);
        _textures.Clear();
    }
}
