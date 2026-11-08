using System.Buffers.Binary;
using System.Text;
using IW4.Game.Assets.ComWorld;
using IW4.Game.Assets.GameMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Material;
using IW4.Game.Math;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // COD4 PS3 c9db8/c9d70: 0x10-byte root and nested image cell at +4.
        private Pointer? LightDef(uint raw, Address cell) => AssetPointer(raw, cell, "lightdef", 16, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                if (_converting && name?.StartsWith(',') == true)
                    throw Unsupported("external COD4 light definition without a converted provider");
                Pointer? image = Image(U32(start + 4), root.Address.Add(4));
                return Named("lightdef", name, root, image?.Dependencies, _converting ? new LightDefAsset
                {
                    Name = name is null ? null : _ownedNamePrefix + name, Image = image?.Value as GfxImageAsset,
                    SamplerState = (MaterialSamplerState)_data[start + 8],
                    Pad09To0B = _data.AsSpan(start + 9, 3).ToArray(),
                    LmapLookupStart = U32(start + 12)
                } : null);
            }
            finally { Pop(); }
        });

        // COD4 PS3 c7a70/c79e0/c7928: root and primary-light rows match IW4.
        private Pointer? ComWorld(uint raw, Address cell) => AssetPointer(raw, cell, "comworld", 16, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                int count = Count(U32(start + 8));
                var lights = new List<ComPrimaryLight>();
                if (U32(start + 12) != 0)
                {
                    Align(4);
                    Region rows = ReadBytes(Size(count, 68));
                    for (int index = 0; index < count; index++)
                    {
                        int item = rows.FileOffset + index * 68;
                        string? definition = ReadString(U32(item + 64));
                        if (_converting) lights.Add(new()
                        {
                            Type = (GfxLightType)_data[item], CanUseShadowMapRaw = _data[item + 1],
                            Exponent = _data[item + 2], Unused = _data[item + 3],
                            Color = MapVec3(item + 4), Dir = MapVec3(item + 16), Origin = MapVec3(item + 28),
                            Radius = MapFloat(item + 40), CosHalfFovOuter = MapFloat(item + 44),
                            CosHalfFovInner = MapFloat(item + 48), CosHalfFovExpanded = MapFloat(item + 52),
                            RotationLimit = MapFloat(item + 56), TranslationLimit = MapFloat(item + 60),
                            DefName = string.IsNullOrEmpty(definition) ? definition : _ownedNamePrefix + definition
                        });
                    }
                }
                return Named("comworld", name, root, value: _converting ? new ComWorldAsset
                {
                    Name = name, IsInUse = (int)U32(start + 4),
                    PrimaryLightCount = count, PrimaryLights = lights
                } : null);
            }
            finally { Pop(); }
        });

        // COD4 PS3 c82f0 has only its XString; IW4 adds optional glass data.
        private Pointer? GameWorld(uint raw, Address cell) => AssetPointer(raw, cell, "gameworldmp", 4, root =>
        {
            Push(4);
            try
            {
                string? name = ReadString(U32(root.FileOffset));
                return Named("gameworldmp", name, root, value: _converting ? new GameWorldMpAsset
                {
                    Name = name, GlassData = new GGlassData()
                } : null);
            }
            finally { Pop(); }
        });

        // COD4 PS3 c7e10: source entity text is presence-controlled, length at +8.
        private Pointer? MapEnts(uint raw, Address cell) => AssetPointer(raw, cell, "mapents", 12, root =>
        {
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? name = ReadString(U32(start));
                int length = Count(U32(start + 8));
                byte[] text = U32(start + 4) == 0 ? [] : Bytes(ReadBytes(length)).ToArray();
                return Named("mapents", name, root, value: _converting ? new MapEntsAsset
                {
                    Name = name, EntityStringBytes = text, NumEntityChars = text.Length,
                    EntityString = Encoding.Latin1.GetString(text).TrimEnd('\0')
                } : null);
            }
            finally { Pop(); }
        });

        private float MapFloat(int offset) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset, 4));
        private Vec3 MapVec3(int offset) => new() { X = MapFloat(offset), Y = MapFloat(offset + 4), Z = MapFloat(offset + 8) };
    }
}
