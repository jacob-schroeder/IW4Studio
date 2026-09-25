using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

/// <summary>OpenGL handles for one retained Walk viewmodel; caller owns the current context.</summary>
internal sealed class WalkPlayerGl
{
    private readonly Dictionary<string, uint> _textures = new(StringComparer.Ordinal);
    internal uint VertexArray { get; private set; }
    private uint _vertexBuffer;

    internal static unsafe WalkPlayerGl Create(GL gl, WalkPlayerPreview preview)
    {
        var result = new WalkPlayerGl();
        try
        {
            result.VertexArray = gl.GenVertexArray();
            result._vertexBuffer = gl.GenBuffer();
            gl.BindVertexArray(result.VertexArray);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, result._vertexBuffer);
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(preview.VertexCount * sizeof(SceneVertex)), null, BufferUsageARB.DynamicDraw);
            for (uint attribute = 0; attribute < 4; attribute++) gl.EnableVertexAttribArray(attribute);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)12);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)24);
            gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)sizeof(SceneVertex), (void*)32);
            gl.BindVertexArray(0);
            foreach (string material in preview.Batches.Select(batch => batch.Material))
            {
                var texture = preview.Texture(material);
                uint handle = gl.GenTexture();
                result._textures.Add(material, handle);
                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, handle);
                gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
                gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                fixed (byte* pixels = texture.Pixels)
                    gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                        (uint)texture.Width, (uint)texture.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    (int)TextureWrapMode.Repeat);
                gl.GenerateMipmap(TextureTarget.Texture2D);
            }
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            return result;
        }
        catch
        {
            result.Delete(gl);
            throw;
        }
    }

    internal unsafe void UploadFrame(GL gl, SceneVertex[] vertices)
    {
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (SceneVertex* data = vertices)
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                (nuint)(vertices.Length * sizeof(SceneVertex)), data);
    }

    internal uint Texture(string material) => _textures[material];

    internal void Delete(GL gl)
    {
        foreach (uint texture in _textures.Values) gl.DeleteTexture(texture);
        _textures.Clear();
        if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
        if (VertexArray != 0) gl.DeleteVertexArray(VertexArray);
        _vertexBuffer = VertexArray = 0;
    }
}
