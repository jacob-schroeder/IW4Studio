using System.Numerics;
using Iw4Radiant.MapSource;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneLighting
{
    private const string NoLightsNotice = "No lights to preview. Add a light or turn off Preview lights.";
    private uint _texture;
    private SceneLight[] _lights = [];
    private int _maximumTextureSize;
    private int _countLocation, _dataLocation;
    private string? _capacityNotice, _omissionNotice;

    internal IReadOnlyList<SceneLight> Lights => _lights;

    internal string? GetNotice(bool sunlightAvailable)
    {
        string? notice = _capacityNotice ?? (_lights.Length == 0 && !sunlightAvailable ? NoLightsNotice : null);
        return _omissionNotice is null ? notice : notice is null ? _omissionNotice : $"{notice} {_omissionNotice}";
    }

    internal void Initialize(GL gl, uint program)
    {
        _countLocation = gl.GetUniformLocation(program, "uLightCount");
        _dataLocation = gl.GetUniformLocation(program, "uLightData");
        _maximumTextureSize = gl.GetInteger(GetPName.MaxTextureSize);
        _texture = gl.GenTexture();
        gl.ActiveTexture(TextureUnit.Texture1);
        try
        {
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        finally
        {
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal unsafe void Update(GL gl, MapDocument document)
    {
        List<SceneLight> lights = [];
        int omitted = 0;
        string? firstError = null;
        foreach (MapEntity entity in document.Entities)
        {
            if (entity.ClassName != "light")
                continue;
            if (SceneLight.TryCreate(document, entity, out SceneLight light, out string? error))
                lights.Add(light);
            else if (error is not null)
            {
                omitted++;
                firstError ??= error;
            }
        }
        _capacityNotice = null;
        if (lights.Count > _maximumTextureSize)
        {
            _capacityNotice = $"Light preview unavailable: {lights.Count} lights exceed this GPU's limit of {_maximumTextureSize}. Turn off Preview lights to view textures.";
            lights.Clear();
        }
        _lights = lights.ToArray();
        // One row per light: position/radius, color/exponent, direction/outer
        // half-angle, then inner half-angle/spotlight flag. Angles are radians.
        Vector4[] data = new Vector4[Math.Max(_lights.Length, 1) * 4];
        for (int index = 0; index < _lights.Length; index++)
        {
            SceneLight light = _lights[index];
            data[index * 4] = new Vector4(light.Origin, light.Radius);
            data[index * 4 + 1] = new Vector4(light.Color, light.Exponent);
            data[index * 4 + 2] = new Vector4(light.Direction, light.OuterAngle);
            data[index * 4 + 3] = new Vector4(light.InnerAngle, light.IsSpotlight ? 1 : 0, 0, 0);
        }
        gl.ActiveTexture(TextureUnit.Texture1);
        try
        {
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            fixed (Vector4* pointer = data)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, 4, (uint)Math.Max(_lights.Length, 1),
                    0, PixelFormat.Rgba, PixelType.Float, pointer);
        }
        finally
        {
            gl.ActiveTexture(TextureUnit.Texture0);
        }
        _omissionNotice = omitted == 0 ? null : $"{omitted} {(omitted == 1 ? "light" : "lights")} omitted. {firstError}";
    }

    internal void Bind(GL gl, bool shadowsAvailable)
    {
        gl.Uniform1(_countLocation, shadowsAvailable ? _lights.Length : 0);
        gl.Uniform1(_dataLocation, 1);
        gl.ActiveTexture(TextureUnit.Texture1);
        try
        {
            gl.BindTexture(TextureTarget.Texture2D, _texture);
        }
        finally
        {
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    internal void Clear(GL gl)
    {
        if (_texture != 0)
            gl.DeleteTexture(_texture);
        ForgetHandle();
    }

    internal void ForgetHandle()
    {
        _texture = 0;
        _lights = [];
        _capacityNotice = _omissionNotice = null;
    }
}
