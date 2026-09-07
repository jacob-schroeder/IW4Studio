using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;

namespace MapConverter.Game.IW3.PC.Conversion;

/// <summary>
/// Authors the fixed IW4 PS3 loading-screen graph. The globally loaded IW4
/// zones supply the native 2d technique set and default image.
/// </summary>
internal static class Iw4LoadZoneBuilder
{
    private const uint LanguageMask = 1;
    private const string TechniqueSetName = ",2d";
    private const string DefaultImageName = ",default";
    internal const int OwnedMaterialCount = 3;

    internal static byte[] Build(
        string targetZoneName,
        GfxImageAsset sourceImage,
        IReadOnlyList<ImageFileStreamLanguageReferences> imageStreamReferences,
        RawFileAsset targetRawFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(imageStreamReferences);
        ArgumentNullException.ThrowIfNull(targetRawFile);

        if (!string.Equals(targetRawFile.Name, targetZoneName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Load-zone rawfile '{targetRawFile.Name}' does not match target " +
                $"zone '{targetZoneName}'.");
        }
        if (sourceImage.Name is null ||
            sourceImage.Name.StartsWith(",", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The loadscreen requires an owned source image definition.");
        }
        if (sourceImage.PayloadByteCount != 0 || sourceImage.PayloadBytes.Count != 0)
        {
            throw new InvalidDataException(
                $"Loadscreen image '{sourceImage.Name}' must be streamed, not resident.");
        }
        if (sourceImage.TextureSemantic != TextureSemantic.TwoDimensional)
        {
            throw new InvalidDataException(
                $"Loadscreen image '{sourceImage.Name}' must use the two-dimensional texture semantic.");
        }

        int[] streamPartByteCounts =
            GfxImageStreamData.ValidateProfileAndComputePartByteCounts(
                sourceImage.StreamData);
        if (imageStreamReferences.Count != 1 ||
            imageStreamReferences[0].LanguageMask != LanguageMask)
        {
            throw new InvalidDataException(
                "The loadscreen requires exactly the PS3 default-language imagefile references.");
        }
        ImageFileStreamLanguageReferences languageReferences =
            imageStreamReferences[0];
        for (int index = 0; index < streamPartByteCounts.Length; index++)
        {
            if (languageReferences.References[index].ByteLength !=
                streamPartByteCounts[index])
            {
                throw new InvalidDataException(
                    $"Loadscreen stream part {index} has a mismatched imagefile byte length.");
            }
        }

        var techniqueSet = new MaterialTechniqueSetAsset
        {
            Name = TechniqueSetName
        };
        var defaultImage = new GfxImageAsset
        {
            Name = DefaultImageName
        };
        GfxImageAsset targetImage = CloneStreamedImage(
            sourceImage,
            $"loadscreen_{targetZoneName}");
        MaterialAsset victoryBackdrop = CreateBackdropMaterial(
            "$victorybackdrop",
            0x8800180080000000,
            defaultImage,
            MaterialSamplerState.FilterNearest,
            TextureSemantic.ColorMap,
            0x18124812,
            techniqueSet,
            (MaterialSortKey)34);
        MaterialAsset defeatBackdrop = CreateBackdropMaterial(
            "$defeatbackdrop",
            0x8800180000000000,
            defaultImage,
            MaterialSamplerState.FilterNearest,
            TextureSemantic.ColorMap,
            0x18124812,
            techniqueSet,
            (MaterialSortKey)34);
        MaterialAsset levelBriefing = CreateBackdropMaterial(
            "$levelbriefing",
            0x8800180040000000,
            targetImage,
            MaterialSamplerState.FilterLinear | MaterialSamplerState.ClampMask,
            TextureSemantic.TwoDimensional,
            0x18128812,
            techniqueSet,
            (MaterialSortKey)34);

        BaseAsset[] roots =
        [
            techniqueSet,
            victoryBackdrop,
            defeatBackdrop,
            levelBriefing,
            targetRawFile
        ];
        var providers = new LinkAssetPool(
            roots
                .Concat<BaseAsset>([defaultImage, targetImage])
                .Select(asset =>
                    new LinkAssetProviderSource(
                        asset,
                        imageStreamReferences: ReferenceEquals(asset, targetImage)
                            ? imageStreamReferences
                            : null)
                    .AsAuthoredDetached()));
        LinkRoot[] linkRoots = roots
            .Select((asset, index) => CreateRoot(index, asset))
            .ToArray();
        ZoneLinkResult link = new ZoneLinker().Link(new ZoneLinkRequest(
            providers,
            linkRoots,
            LanguageMask,
            LanguageMask,
            []));
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
        {
            throw new InvalidDataException(
                "Load fastfile link failed: " + string.Join("; ", link.Errors));
        }
        ValidateStreamTable(link.ImageStreamLanguageTables, languageReferences);

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } bytes)
        {
            throw new InvalidDataException(
                "Load fastfile packaging failed: " +
                string.Join("; ", package.Errors.Select(error =>
                    $"{error.Code}: {error.Message}")));
        }
        return bytes.ToArray();
    }

    internal static MaterialAsset CreateBackdropMaterial(
        string name,
        ulong drawSurf,
        GfxImageAsset image,
        MaterialSamplerState samplerState,
        TextureSemantic semantic,
        uint stateBits0,
        MaterialTechniqueSetAsset techniqueSet,
        MaterialSortKey sortKey)
    {
        MaterialStateBitsEntry[] stateEntries = Enumerable
            .Repeat(new MaterialStateBitsEntry(byte.MaxValue),
                MaterialAsset.TechniqueSlotCount)
            .ToArray();
        stateEntries[(int)MaterialTechniqueType.Unlit] =
            new MaterialStateBitsEntry(0);
        var inlineTechniqueState = new ushort[MaterialAsset.TechniqueSlotCount];
        inlineTechniqueState[(int)MaterialTechniqueType.Unlit] = 0x70f8;

        return new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = name,
                GameFlags = MaterialGameFlags.None,
                SortKey = sortKey,
                TextureAtlasRowCount = 1,
                TextureAtlasColumnCount = 1,
                DrawSurf = new GfxDrawSurf(drawSurf),
                SurfaceTypeBits = 0,
                HashIndex = 0,
                Pad16 = 0
            },
            StateBitsEntries = stateEntries,
            TextureCount = 1,
            ConstantCount = 0,
            StateBitsCount = 1,
            StateFlags = MaterialStateFlags.None,
            CameraRegion = GfxCameraRegionType.Count,
            XStringCount = 0,
            Pad43 = 0,
            InlineTechniqueSlotStateBits = inlineTechniqueState,
            Pad8E = 0,
            RuntimeTechniqueSlotStateBits = [],
            TechniqueSet = techniqueSet,
            Textures =
            [
                new MaterialTextureDef
                {
                    NameHash = 0xa0ab1041,
                    NameStart = 0x63,
                    NameEnd = 0x70,
                    SamplerState = samplerState,
                    Semantic = semantic,
                    Image = image
                }
            ],
            Constants = [],
            StateBits =
            [
                new GfxStateBits
                {
                    LoadBits = [stateBits0, 0xe00e0002],
                    CommandWordCount = 0
                }
            ],
            XStrings = []
        };
    }

    private static GfxImageAsset CloneStreamedImage(
        GfxImageAsset source,
        string targetName) => new()
    {
        Format = source.Format,
        LevelCount = source.LevelCount,
        DimensionCount = source.DimensionCount,
        MultiFaceControl = source.MultiFaceControl,
        TextureControl1 = source.TextureControl1,
        Width = source.Width,
        Height = source.Height,
        Depth = source.Depth,
        MemoryLocation = source.SerializedMemoryLocation,
        MinLodControl = source.MinLodControl,
        RenderTargetPitch = source.RenderTargetPitch,
        PixelsOffset = source.SerializedPixelsOffset,
        MapType = source.MapType,
        TextureSemantic = source.TextureSemantic,
        Category = source.Category,
        UseSrgbReads = source.UseSrgbReads,
        CardMemory = source.CardMemory,
        BaseWidth = source.BaseWidth,
        BaseHeight = source.BaseHeight,
        BaseDepth = source.BaseDepth,
        BaseLevelCount = source.BaseLevelCount,
        Cached = source.Cached,
        StreamData = source.StreamData.Select(entry => new GfxImageStreamData(
            entry.Width,
            entry.Height,
            entry.LevelSizeAndOffset)).ToArray(),
        PayloadByteCount = 0,
        PayloadBytes = [],
        Name = targetName
    };

    private static LinkRoot CreateRoot(int index, BaseAsset asset)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException(
                $"{asset.SerializedAssetType} load root has no serialized name.");
        return new LinkRoot(
            $"mapconverter:load:{index}:{asset.SerializedAssetType}",
            asset.SerializedAssetType,
            name.StartsWith(",", StringComparison.Ordinal)
                ? LinkRootIntent.External
                : LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            name,
            opaqueHeader: null);
    }

    private static void ValidateStreamTable(
        IReadOnlyList<DbHeaderImageStreamLanguageTable> tables,
        ImageFileStreamLanguageReferences expected)
    {
        if (tables.Count != 1 || tables[0].LanguageMask != LanguageMask)
        {
            throw new InvalidDataException(
                "Linked loadscreen has an unexpected image-stream language table.");
        }
        IReadOnlyList<DbHeaderImageStreamEntry> actual =
            tables[0].ImageStreamEntries;
        if (actual.Count != expected.References.Count)
        {
            throw new InvalidDataException(
                "Linked loadscreen has an unexpected number of image-stream rows.");
        }
        for (int index = 0; index < actual.Count; index++)
        {
            DbHeaderImageStreamEntry expectedEntry =
                expected.References[index].Entry;
            DbHeaderImageStreamEntry actualEntry = actual[index];
            if (actualEntry.FileIndex != expectedEntry.FileIndex ||
                actualEntry.SourceStart != expectedEntry.SourceStart ||
                actualEntry.SourceEnd != expectedEntry.SourceEnd ||
                actualEntry.BlockOffset != expectedEntry.BlockOffset ||
                actualEntry.StreamOffset != expectedEntry.StreamOffset)
            {
                throw new InvalidDataException(
                    $"Linked loadscreen stream row {index} differs from its imagefile reference.");
            }
        }
    }
}
