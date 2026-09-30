using System.Numerics;
using Iw4Radiant.MapSource;
using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal sealed class SceneLighting
{
    private const string NoLightsNotice = "No lights to preview. Add a light or turn off Preview lights.";
    private uint _texture;
    private uint _primaryFalloffTexture;
    private MapLight[] _lights = [];
    private object[] _sources = [];
    private int _sweepIndex = -1;
    private Vector3 _sweepCenterDirection;
    private int _maximumTextureSize;
    private int _countLocation, _dataLocation, _falloffLocation;
    private string? _capacityNotice, _omissionNotice;

    internal IReadOnlyList<MapLight> Lights => _lights;

    internal string? GetNotice(bool sunlightAvailable)
    {
        string? notice = _capacityNotice ?? (_lights.Length == 0 && !sunlightAvailable ? NoLightsNotice : null);
        return _omissionNotice is null ? notice : notice is null ? _omissionNotice : $"{notice} {_omissionNotice}";
    }

    internal void Initialize(GL gl, uint program)
    {
        _countLocation = gl.GetUniformLocation(program, "uLightCount");
        _dataLocation = gl.GetUniformLocation(program, "uLightData");
        _falloffLocation = gl.GetUniformLocation(program, "uPrimaryFalloff");
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

    internal unsafe void Update(GL gl, EditorScene scene)
    {
        List<MapLight> lights = [];
        List<object> sources = [];
        int omitted = 0;
        string? firstError = null;
        foreach (MapEntity entity in scene.Document.Entities)
        {
            if (entity.ClassName != "light")
                continue;
            if (MapLight.TryCreate(entity, scene.ResolveTargets(entity), out MapLight light, out string? error))
            {
                lights.Add(light);
                // Scene entities are display copies; playback selects their authored owner.
                sources.Add(scene.Owner(entity));
            }
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
            sources.Clear();
        }
        _lights = lights.ToArray();
        _sources = sources.ToArray();
        _sweepIndex = -1;
        // One row per light: position/radius, color/exponent, direction/outer cone,
        // then inner cone/spotlight/primary flags. Primary Spot cones use cosines;
        // static Spot cones retain half-angle radians.
        Vector4[] data = new Vector4[Math.Max(_lights.Length, 1) * 4];
        for (int index = 0; index < _lights.Length; index++)
        {
            MapLight light = _lights[index];
            data[index * 4] = new Vector4(light.Origin, light.Radius);
            data[index * 4 + 1] = new Vector4(light.Color, light.Exponent);
            bool primarySpot = light.IsPrimary && light.IsSpotlight;
            data[index * 4 + 2] = new Vector4(light.Direction,
                primarySpot ? MathF.Cos(light.OuterAngle) : light.OuterAngle);
            data[index * 4 + 3] = new Vector4(primarySpot ? MathF.Cos(light.InnerAngle) : light.InnerAngle,
                light.IsSpotlight ? 1 : 0,
                light.IsPrimary ? 1 : 0, 0);
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

    internal unsafe bool UpdateInfluence(GL gl, EditorScene scene, MapEntity source)
    {
        int index = Array.FindIndex(_sources, entity => ReferenceEquals(entity, source));
        if (index < 0 || !MapLight.TryCreate(source, scene.ResolveTargets(source), out MapLight light, out _))
            return false;
        MapLight previous = _lights[index];
        _lights[index] = light;
        if (_sweepIndex == index) _sweepCenterDirection = light.Direction;
        if (previous.Direction == light.Direction && previous.InnerAngle == light.InnerAngle &&
            previous.OuterAngle == light.OuterAngle) return true;

        // Cone edits reuse the fixed-origin shadow cube and replace only two lighting texels.
        bool primarySpot = light.IsPrimary && light.IsSpotlight;
        Vector4* data = stackalloc Vector4[2];
        data[0] = new Vector4(light.Direction, primarySpot ? MathF.Cos(light.OuterAngle) : light.OuterAngle);
        data[1] = new Vector4(primarySpot ? MathF.Cos(light.InnerAngle) : light.InnerAngle,
            light.IsSpotlight ? 1 : 0, light.IsPrimary ? 1 : 0, 0);
        gl.ActiveTexture(TextureUnit.Texture1);
        try
        {
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, 2, index, 2, 1,
                PixelFormat.Rgba, PixelType.Float, data);
        }
        finally { gl.ActiveTexture(TextureUnit.Texture0); }
        return true;
    }

    internal void UpdateSweep(GL gl, MapEntity? previewEntity, double seconds)
    {
        int index = previewEntity is null ? -1 : Array.FindIndex(_sources, entity => ReferenceEquals(entity, previewEntity));
        if (index >= 0 && !_lights[index].IsMoving) index = -1;
        if (_sweepIndex >= 0 && _sweepIndex != index)
        {
            _lights[_sweepIndex] = _lights[_sweepIndex] with { Direction = _sweepCenterDirection };
            UploadDirection(gl, _sweepIndex);
        }
        if (index < 0)
        {
            _sweepIndex = -1;
            return;
        }
        if (_sweepIndex != index) _sweepCenterDirection = _lights[index].Direction;
        _sweepIndex = index;
        Vector3 direction = (_lights[index] with { Direction = _sweepCenterDirection })
            .DirectionAt(seconds + MapLight.SweepWarmupSeconds);
        if (_lights[index].Direction == direction) return;
        _lights[index] = _lights[index] with { Direction = direction };
        UploadDirection(gl, index);
    }

    private unsafe void UploadDirection(GL gl, int index)
    {
        MapLight light = _lights[index];
        Vector4 data = new(light.Direction,
            light.IsPrimary && light.IsSpotlight ? MathF.Cos(light.OuterAngle) : light.OuterAngle);
        gl.ActiveTexture(TextureUnit.Texture1);
        try
        {
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, 2, index, 1, 1,
                PixelFormat.Rgba, PixelType.Float, &data);
        }
        finally { gl.ActiveTexture(TextureUnit.Texture0); }
    }

    internal void Bind(GL gl, bool shadowsAvailable)
    {
        gl.Uniform1(_countLocation, shadowsAvailable ? _lights.Length : 0);
        gl.Uniform1(_dataLocation, 1);
        if (shadowsAvailable && _lights.Any(light => light.IsPrimary))
            BindPrimaryFalloff(gl);
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

    internal unsafe void BindPrimaryFalloff(GL gl)
    {
        gl.Uniform1(_falloffLocation, 9);
        if (_primaryFalloffTexture == 0)
        {
            ReadOnlySpan<byte> samples = PrimaryLocalLightProfile.Samples;
            _primaryFalloffTexture = gl.GenTexture();
            gl.ActiveTexture(TextureUnit.Texture9);
            gl.BindTexture(TextureTarget.Texture2D, _primaryFalloffTexture);
            gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pointer = samples)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
                    PrimaryLocalLightProfile.Width, 1, 0, PixelFormat.Red, PixelType.UnsignedByte, pointer);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        gl.ActiveTexture(TextureUnit.Texture9);
        gl.BindTexture(TextureTarget.Texture2D, _primaryFalloffTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    internal void Clear(GL gl)
    {
        if (_texture != 0)
            gl.DeleteTexture(_texture);
        if (_primaryFalloffTexture != 0)
            gl.DeleteTexture(_primaryFalloffTexture);
        ForgetHandle();
    }

    internal void ForgetHandle()
    {
        _texture = 0;
        _primaryFalloffTexture = 0;
        _lights = [];
        _sources = [];
        _sweepIndex = -1;
        _capacityNotice = _omissionNotice = null;
    }
}
