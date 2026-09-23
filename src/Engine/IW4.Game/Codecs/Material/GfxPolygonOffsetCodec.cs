using IW4.Game.Assets.Material;

namespace IW4.Game.Codecs.Material;

/// <summary>PS3 polygon-offset ordinals expanded into RSX factor and units.</summary>
public static class GfxPolygonOffsetCodec
{
    public static (float Factor, float Units) Decode(GfxPolygonOffset value)
    {
        if (value is not (GfxPolygonOffset.Disabled or GfxPolygonOffset.Offset1 or GfxPolygonOffset.Offset2))
            throw new ArgumentOutOfRangeException(nameof(value), "Polygon offset must be disabled, offset1, or offset2.");
        return (-(float)value, (float)value * -50f);
    }
}
