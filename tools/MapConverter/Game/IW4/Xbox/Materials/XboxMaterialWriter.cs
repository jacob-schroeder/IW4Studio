using IW4.Game.Assets.Material;
using MapConverter.Game.IW4.Xbox.FastFiles;
using static MapConverter.Game.IW4.Xbox.FastFiles.XboxWire;

namespace MapConverter.Game.IW4.Xbox.Materials;

internal static class XboxMaterialWriter
{
    internal static string NativeTechniqueSetName(string sourceName)
    {
        string name = sourceName.TrimStart(',');
        return name switch
        {
            "pitch/w_l_sm_r0c0n0" => "w_l_sm_r0c0n0",
            "pitch/wc_l_sm_r0c0q0n0s0p0" => "wc_l_sm_r0c0q0n0s0p0",
            "pitch/wc_l_sm_r0c0n0s0" => "wc_l_sm_r0c0n0s0",
            "pitch/wc_l_sm_r0c0n0sf0" => "wc_l_sm_r0c0n0sf0",
            "pitch/w_l_sm_t0c0n0sf0" => "w_l_sm_t0c0n0sf0",
            "pitch/wc_l_sm_r0c0sf0" => "wc_l_sm_r0c0sf0",
            "pitch/wc_l_sm_r0c0n0sf0p0" => "wc_l_sm_r0c0n0sf0p0",
            _ when name.StartsWith("pitch/", StringComparison.Ordinal) =>
                throw new NotSupportedException($"Xbox has no mapped technique set for '{sourceName}'."),
            _ => name
        };
    }

    internal static void Write(XboxZoneStream zone, MaterialAsset material)
    {
        var info = material.Info;
        string name = info.Name ?? throw new InvalidDataException("Unnamed material.");
        byte[] root = new byte[0x58];
        U32(root, 0, Inline);
        root[4] = (byte)info.GameFlags; root[5] = (byte)info.SortKey;
        root[6] = info.TextureAtlasRowCount; root[7] = info.TextureAtlasColumnCount;
        // Retail zones leave drawSurf zero. The Xbox renderer builds it when registering the material.
        U32(root, 16, (uint)info.SurfaceTypeBits);
        if (material.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount)
            throw new InvalidDataException($"Material '{name}' has an incomplete technique state table.");
        // The four PS3 instanced-lighting slots (23..26) are absent from the Xenon table.
        for (int i = 0; i < 33; i++) root[0x18 + i] = material.StateBitsEntries[i < 23 ? i : i + 4].StateBitsIndex;
        root[0x39] = checked((byte)material.Textures.Count);
        root[0x3a] = checked((byte)material.Constants.Count);
        root[0x3b] = checked((byte)material.StateBits.Count);
        root[0x3c] = (byte)material.StateFlags; root[0x3d] = (byte)material.CameraRegion;
        root[0x3e] = checked((byte)material.SubMaterialNames.Count);
        if (material.TechniqueSet?.Name is string set)
            U32(root, 0x40, zone.Asset(XboxAssetType.TechniqueSet, NativeTechniqueSetName(set)));
        U32(root, 0x44, Present(material.Textures.Count)); U32(root, 0x48, Present(material.Constants.Count));
        U32(root, 0x4c, Present(material.StateBits.Count)); U32(root, 0x50, Present(material.SubMaterialNames.Count));
        zone.Write(root, 4, XboxBlock.Temp);
        zone.String(name);
        if (material.Textures.Count != 0)
        {
            byte[] textures = new byte[material.Textures.Count * 12];
            for (int i = 0; i < material.Textures.Count; i++)
            {
                var t = material.Textures[i];
                if (t.Water != null) throw new NotSupportedException($"Xbox water simulation textures in '{name}' require conversion.");
                int at = i * 12;
                U32(textures, at, t.NameHash);
                textures[at + 4] = t.NameStart; textures[at + 5] = t.NameEnd;
                textures[at + 6] = (byte)t.SamplerState; textures[at + 7] = (byte)t.Semantic;
                if (t.Image?.Name is string image) U32(textures, at + 8, zone.Asset(XboxAssetType.Image, image));
            }
            zone.Write(textures, 4);
            zone.Reserve(material.Textures.Count * sizeof(uint), 4, XboxBlock.Callback);
        }
        if (material.Constants.Count != 0)
        {
            byte[] constants = new byte[material.Constants.Count * 32];
            for (int i = 0; i < material.Constants.Count; i++)
            {
                var c = material.Constants[i];
                int at = i * 32;
                U32(constants, at, c.NameHash);
                c.NameBytes.ToArray().CopyTo(constants, at + 4);
                Float(constants, at + 16, c.Literal.X); Float(constants, at + 20, c.Literal.Y);
                Float(constants, at + 24, c.Literal.Z); Float(constants, at + 28, c.Literal.W);
            }
            zone.Write(constants, 16);
        }
        if (material.StateBits.Count != 0)
        {
            byte[] states = new byte[material.StateBits.Count * 8];
            for (int i = 0; i < material.StateBits.Count; i++)
            {
                var bits = material.StateBits[i].LoadBits;
                if (bits.Count != 2) throw new InvalidDataException($"Material '{name}' has missing state bits at {i}.");
                U32(states, i * 8, bits[0]); U32(states, i * 8 + 4, bits[1]);
            }
            zone.Write(states, 4);
        }
        if (material.SubMaterialNames.Count != 0)
        {
            byte[] names = new byte[material.SubMaterialNames.Count * 4];
            for (int i = 0; i < material.SubMaterialNames.Count; i++)
                if (material.SubMaterialNames[i].Value != null) U32(names, i * 4, Inline);
            zone.Write(names, 4);
            foreach (var item in material.SubMaterialNames)
                if (item.Value is string s) zone.String(s);
        }
    }
}
