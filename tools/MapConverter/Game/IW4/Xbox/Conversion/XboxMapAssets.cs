using IW4.Game.Assets.ComWorld;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.GameMap;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.RawFile;
using MapConverter.Game.IW4.Xbox.FastFiles;
using static MapConverter.Game.IW4.Xbox.FastFiles.XboxWire;

namespace MapConverter.Game.IW4.Xbox.Conversion;

internal static class XboxMapAssets
{
    private static void Rows<T>(XboxZoneStream zone, IReadOnlyList<T> rows, int stride, Action<byte[], int, T> encode)
    {
        if (rows.Count == 0) return;
        byte[] data = new byte[checked(rows.Count * stride)];
        for (int i = 0; i < rows.Count; i++) encode(data, i * stride, rows[i]);
        zone.Write(data, 4);
    }

    internal static void MapEnts(XboxZoneStream zone, MapEntsAsset map)
    {
        var trigger = map.Trigger;
        byte[] root = new byte[0x2c];
        U32(root, 0, Inline); U32(root, 4, Present(map.EntityStringBytes.Count));
        I32(root, 8, map.EntityStringBytes.Count);
        I32(root, 12, trigger.Models.Count); U32(root, 16, Present(trigger.Models.Count));
        I32(root, 20, trigger.Hulls.Count); U32(root, 24, Present(trigger.Hulls.Count));
        I32(root, 28, trigger.Slabs.Count); U32(root, 32, Present(trigger.Slabs.Count));
        U32(root, 36, Present(map.Stages.Count)); root[40] = checked((byte)map.Stages.Count);
        zone.Write(root, 4, XboxBlock.Temp); zone.String(map.Name);
        zone.Write(map.EntityStringBytes.ToArray());
        Rows(zone, trigger.Models, 8, (b, i, m) => {
            I32(b, i, m.Contents); U16(b, i + 4, m.HullCount); U16(b, i + 6, m.FirstHull);
        });
        Rows(zone, trigger.Hulls, 32, (b, i, h) => {
            Vector(b, i, h.Bounds.MidPoint); Vector(b, i + 12, h.Bounds.HalfSize);
            I32(b, i + 24, h.Contents); U16(b, i + 28, h.SlabCount); U16(b, i + 30, h.FirstSlab);
        });
        Rows(zone, trigger.Slabs, 20, (b, i, s) => {
            Vector(b, i, s.Dir); Float(b, i + 12, s.MidPoint); Float(b, i + 16, s.HalfSize);
        });
        Rows(zone, map.Stages, 20, (b, i, s) => {
            if (s.StageName != null) U32(b, i, Inline);
            Vector(b, i + 4, s.Origin); U16(b, i + 16, s.TriggerIndex); b[i + 18] = s.SunPrimaryLightIndex;
        });
        foreach (var stage in map.Stages)
            if (stage.StageName is string name) zone.String(name);
    }

    internal static void ComWorld(XboxZoneStream zone, ComWorldAsset world)
    {
        byte[] root = new byte[16];
        U32(root, 0, Inline); I32(root, 4, world.IsInUse);
        I32(root, 8, world.PrimaryLights.Count); U32(root, 12, Present(world.PrimaryLights.Count));
        zone.Write(root, 4, XboxBlock.Temp); zone.String(world.Name);
        Rows(zone, world.PrimaryLights, 68, (b, i, light) => {
            b[i] = (byte)light.Type; b[i + 1] = light.CanUseShadowMapRaw; b[i + 2] = light.Exponent;
            Vector(b, i + 4, light.Color); Vector(b, i + 16, light.Dir); Vector(b, i + 28, light.Origin);
            Float(b, i + 40, light.Radius); Float(b, i + 44, light.CosHalfFovOuter);
            Float(b, i + 48, light.CosHalfFovInner); Float(b, i + 52, light.CosHalfFovExpanded);
            Float(b, i + 56, light.RotationLimit); Float(b, i + 60, light.TranslationLimit);
            if (light.DefName != null) U32(b, i + 64, Inline);
        });
        foreach (var light in world.PrimaryLights) if (light.DefName is string text) zone.String(text);
    }

    internal static void GameWorld(XboxZoneStream zone, GameWorldMpAsset world)
    {
        byte[] root = new byte[8];
        U32(root, 0, Inline);
        if (world.GlassData != null) U32(root, 4, Inline);
        zone.Write(root, 4, XboxBlock.Temp); zone.String(world.Name);
        if (world.GlassData is not { } glass) return;
        if (glass.GlassPieces.Count != 0 || glass.GlassNames.Count != 0)
            throw new NotSupportedException($"Xbox destructible glass in '{world.Name}' requires conversion.");
        byte[] header = new byte[128];
        U16(header, 8, glass.DamageToWeaken); U16(header, 10, glass.DamageToDestroy);
        zone.Write(header, 4);
    }

    internal static void FxWorld(XboxZoneStream zone, FxWorldAsset world)
    {
        var g = world.GlassSystem;
        if (g.DefCount != 0 || g.InitPieceCount != 0 || g.PieceLimit != 0 || g.GeoDataLimit != 0 ||
            g.PieceWordCount != 0 || g.GeoDataCount != 0 || g.InitGeoDataCount != 0)
            throw new NotSupportedException($"Xbox destructible glass in '{world.Name}' requires conversion.");
        byte[] root = new byte[116];
        U32(root, 0, Inline); I32(root, 4, g.Time); I32(root, 8, g.PrevTime);
        U32(root, 28, g.CellCount); U32(root, 32, g.ActivePieceCount); U32(root, 36, g.FirstFreePiece);
        root[104] = g.NeedToCompactData; root[105] = g.InitCount;
        Float(root, 108, g.EffectChanceAccum); I32(root, 112, g.LastPieceDeletionTime);
        zone.Write(root, 4, XboxBlock.Temp); zone.String(world.Name);
    }

    internal static void RawFile(XboxZoneStream zone, RawFileAsset file)
    {
        byte[] root = new byte[16];
        U32(root, 0, Inline);
        I32(root, 4, file.CompressedLen); I32(root, 8, file.Len);
        if (file.Buffer != null) U32(root, 12, Inline);
        zone.Write(root, 4, XboxBlock.Temp); zone.String(file.Name);
        if (file.Buffer != null) zone.Write(file.Buffer);
    }

    internal static void Preset(XboxZoneStream zone, PhysPresetAsset p)
    {
        byte[] root = new byte[44];
        U32(root, 0, Inline); I32(root, 4, p.SoundClassIndex); Float(root, 8, p.Mass);
        Float(root, 12, p.Bounce); Float(root, 16, p.Friction);
        Float(root, 20, p.BulletForceScale); Float(root, 24, p.ExplosiveForceScale);
        if (p.SndAliasPrefix != null) U32(root, 28, Inline);
        Float(root, 32, p.PiecesSpreadFraction); Float(root, 36, p.PiecesUpwardVelocity);
        root[40] = p.TempDefaultToCylinder; root[41] = p.PerSurfaceSndAlias;
        zone.Write(root, 4, XboxBlock.Temp); zone.String(p.Name);
        if (p.SndAliasPrefix is string text) zone.String(text);
    }
}
