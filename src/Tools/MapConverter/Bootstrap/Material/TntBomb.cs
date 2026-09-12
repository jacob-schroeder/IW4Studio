using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Database.Streaming;

namespace MapConverter.Bootstrap.Material;

// Fixed PS3 TNT model materials and stock image-package references.
internal static class TntBomb
{
    internal static (MaterialAsset Rigid, MaterialAsset Skinned) Create()
    {
        GfxImageAsset colorMap = CreateImage(
            "tntblock_and_timer_col", TextureSemantic.ColorMap,
            [
                new DbHeaderImageStreamEntry(1u, 41493360u, 41530313u, 27264u, 79391360u, -1),
                new DbHeaderImageStreamEntry(4u, 380306233u, 380386592u, 53504u, 593023232u, -1),
                new DbHeaderImageStreamEntry(4u, 380357927u, 380404197u, 37120u, 593072384u, -1),
                new DbHeaderImageStreamEntry(0u, 0u, 0u, 0u, 0u, -1)
            ]);
        GfxImageAsset normalMap = CreateImage(
            "tntblock_and_timer_nml", TextureSemantic.NormalMap,
            [
                new DbHeaderImageStreamEntry(1u, 41493360u, 41530313u, 32768u, 79396864u, -1),
                new DbHeaderImageStreamEntry(4u, 380357927u, 380386592u, 4352u, 593039616u, -1),
                new DbHeaderImageStreamEntry(4u, 380386592u, 380428820u, 37120u, 593137920u, -1),
                new DbHeaderImageStreamEntry(0u, 0u, 0u, 0u, 0u, -1)
            ]);
        GfxImageAsset specularMap = CreateImage(
            "~tntblock_and_timer_spc-rgb\u0026t~75368643", TextureSemantic.SpecularMap,
            [
                new DbHeaderImageStreamEntry(1u, 41493360u, 41530313u, 38272u, 79402368u, -1),
                new DbHeaderImageStreamEntry(4u, 380357927u, 380386592u, 20736u, 593056000u, -1),
                new DbHeaderImageStreamEntry(4u, 380404197u, 380470353u, 37120u, 593203456u, -1),
                new DbHeaderImageStreamEntry(0u, 0u, 0u, 0u, 0u, -1)
            ]);
        return (
            CreateMaterial("m/mtl_tntbomb", ",m_l_sm_r0c0n0sf0", colorMap, normalMap, specularMap),
            CreateMaterial("mg/mtl_tntbomb", ",mg_l_sm_r0c0n0sf0", colorMap, normalMap, specularMap));
    }

    private static MaterialAsset CreateMaterial(
        string name, string techniqueSetName,
        GfxImageAsset colorMap, GfxImageAsset normalMap, GfxImageAsset specularMap) =>
        new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = name,
                GameFlags = (MaterialGameFlags)80,
                SortKey = MaterialSortKey.Opaque,
                TextureAtlasRowCount = 0x01,
                TextureAtlasColumnCount = 0x01,
                SurfaceTypeBits = MaterialSurfaceTypeBits.Metal,
                HashIndex = 0,
                Pad16 = 0
            },
            StateBitsEntries = [
                new MaterialStateBitsEntry(0x00),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0x01),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x02),
                new MaterialStateBitsEntry(0x03),
                new MaterialStateBitsEntry(0x03),
                new MaterialStateBitsEntry(0x03),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0x04),
                new MaterialStateBitsEntry(0xFF),
                new MaterialStateBitsEntry(0x02)
            ],
            TextureCount = 0x03,
            ConstantCount = 0x02,
            StateBitsCount = 0x05,
            StateFlags = (MaterialStateFlags)121,
            CameraRegion = GfxCameraRegionType.LitOpaque,
            XStringCount = 0x00,
            Pad43 = 0x00,
            Pad8E = 0,
            TechniqueSet = new MaterialTechniqueSetAsset { Name = techniqueSetName },
            Textures = [
                new MaterialTextureDef
                {
                    NameHash = 0x34ECCCB3u,
                    NameStart = 0x73,
                    NameEnd = 0x70,
                    SamplerState = (MaterialSamplerState)11,
                    Semantic = TextureSemantic.SpecularMap,
                    Image = specularMap
                },
                new MaterialTextureDef
                {
                    NameHash = 0x59D30D0Fu,
                    NameStart = 0x6E,
                    NameEnd = 0x70,
                    SamplerState = (MaterialSamplerState)11,
                    Semantic = TextureSemantic.NormalMap,
                    Image = normalMap
                },
                new MaterialTextureDef
                {
                    NameHash = 0xA0AB1041u,
                    NameStart = 0x63,
                    NameEnd = 0x70,
                    SamplerState = (MaterialSamplerState)11,
                    Semantic = TextureSemantic.ColorMap,
                    Image = colorMap
                }
            ],
            Constants = [
                new MaterialConstantDef
                {
                    NameHash = 0x3D9994DCu,
                    NameBytes = [0x65, 0x6E, 0x76, 0x4D, 0x61, 0x70, 0x50, 0x61, 0x72, 0x6D, 0x73, 0x00],
                    Literal = new MaterialVec4(0.07f, 0.33f, 3.3f, 2f)
                },
                new MaterialConstantDef
                {
                    NameHash = 0xB60C3B3Au,
                    NameBytes = [0x63, 0x6F, 0x6C, 0x6F, 0x72, 0x54, 0x69, 0x6E, 0x74, 0x00, 0x00, 0x00],
                    Literal = new MaterialVec4(1f, 1f, 1f, 1f)
                }
            ],
            StateBits = [
                new GfxStateBits
                {
                    LoadBits = [0x00128812u, 0xE49E490Du]
                },
                new GfxStateBits
                {
                    LoadBits = [0x00128812u, 0x0000003Du]
                },
                new GfxStateBits
                {
                    LoadBits = [0x58128812u, 0x0000000Du]
                },
                new GfxStateBits
                {
                    LoadBits = [0x592A892Au, 0xE0040048u]
                },
                new GfxStateBits
                {
                    LoadBits = [0x88128812u, 0xE49E492Cu]
                }
            ]
        };

    private static GfxImageAsset CreateImage(
        string name, TextureSemantic semantic, IReadOnlyList<DbHeaderImageStreamEntry> streamEntries) =>
        new GfxImageAsset
        {
            Format = 0x88,
            LevelCount = 0x01,
            DimensionCount = GfxImageDimension.TwoDimensional,
            MultiFaceControl = 0x00,
            TextureControl1 = 0x0001AAE4u,
            Width = 1,
            Height = 1,
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MinLodControl = 0x00,
            RenderTargetPitch = 0x00000000u,
            PixelsOffset = 0x00000000u,
            MapType = MapType.TwoDimensional,
            TextureSemantic = semantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = 0x00,
            CardMemory = 0x00000000u,
            BaseWidth = 1,
            BaseHeight = 1,
            BaseDepth = 1,
            BaseLevelCount = 0x01,
            Cached = GfxImageCached.Auto,
            StreamData = [
                new GfxImageStreamData(64, 64, 0x1C001580u),
                new GfxImageStreamData(128, 128, 0x20005580u),
                new GfxImageStreamData(256, 256, 0x24015580u),
                new GfxImageStreamData(0, 0, 0x00000000u)
            ],
            StreamEntries = streamEntries,
            PayloadByteCount = 0,
            Name = name
        };
}
