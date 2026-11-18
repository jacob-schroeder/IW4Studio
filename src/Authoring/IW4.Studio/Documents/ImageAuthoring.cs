using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Image;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

public sealed class ImageDraft
{
    private readonly GfxImageAsset _original;
    private readonly ImageState _startingState;
    private ImageState _state;

    internal ImageDraft(GfxImageAsset image)
    {
        _original = image;
        _startingState = _state = new(image, image.TextureSemantic, image.UseSrgbReads);
    }

    private ImageDraft(ImageDraft source)
    {
        _original = source._original;
        _startingState = _state = source._state;
    }

    public GfxImageAsset Image => CreateDefinition();
    public TextureSemantic TextureSemantic => _state.Semantic;
    public bool UsesSrgbReads => (_state.UseSrgbReads & 1) != 0;

    public bool CanEditMetadata =>
        _state.Semantic != TextureSemantic.WaterMap &&
        _state.Pixels.Category is ImageCategory.LoadFromFile or ImageCategory.Raw or ImageCategory.Unknown;

    public bool CanReplacePixels =>
        CanEditMetadata && (GetShape(_state.Pixels) is not null || IsEmptyImage(_state.Pixels));

    internal bool RetainsOriginalPixels => ReferenceEquals(_state.Pixels, _original);

    public ImageDraft Clone() => new(this);

    public void CopyFrom(ImageDraft staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (!ReferenceEquals(_original, staged._original) || _state != staged._startingState)
            throw new InvalidOperationException("The prepared image edit no longer matches the current image.");

        _state = staged._state;
    }

    public void SetTextureSemantic(TextureSemantic semantic)
    {
        RequireEditableMetadata();
        if ((!Enum.IsDefined(semantic) && semantic != _original.TextureSemantic) || semantic == TextureSemantic.WaterMap)
            throw new ArgumentOutOfRangeException(nameof(semantic), semantic, "This image semantic cannot be authored.");

        _state = _state with { Semantic = semantic };
    }

    public void SetUsesSrgbReads(bool enabled)
    {
        RequireEditableMetadata();
        // Bit 0 controls gamma reads. Preserve the other native bits on metadata edits.
        _state = _state with { UseSrgbReads = (byte)((_state.UseSrgbReads & 0xfe) | (enabled ? 1 : 0)) };
    }

    public void ReplacePixels(ImageFileDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!CanReplacePixels)
            throw new InvalidOperationException("Pixel replacement is unavailable for this image.");

        if (GetShape(_state.Pixels) is { } shape && source.Shape != shape)
        {
            throw new InvalidDataException(
                $"The imported image has shape {source.Shape}. This image requires {shape} to preserve its sampler shape.");
        }

        string name = _original.Name ?? throw new InvalidDataException("The image has no name.");
        GfxImageAsset compiled = ImageSourceCompiler.Compile(
            name, source, TextureSemantic, source.UsesSrgbReads ?? UsesSrgbReads);
        _state = new(compiled, compiled.TextureSemantic, compiled.UseSrgbReads);
    }

    internal bool SemanticallyEquals(ImageDraft other)
    {
        if (_state == other._state)
            return true;
        if (_state.Semantic != other._state.Semantic || _state.UseSrgbReads != other._state.UseSrgbReads ||
            RetainsOriginalPixels || other.RetainsOriginalPixels)
            return false;

        GfxImageAsset left = _state.Pixels;
        GfxImageAsset right = other._state.Pixels;
        return left.Name == right.Name && left.MapType == right.MapType &&
            left.Width == right.Width && left.Height == right.Height && left.Depth == right.Depth &&
            left.LevelCount == right.LevelCount && left.PayloadBytes.SequenceEqual(right.PayloadBytes);
    }

    internal GfxImageAsset CreateDefinition()
    {
        GfxImageAsset pixels = _state.Pixels;
        return new GfxImageAsset
        {
            Offset = pixels.Offset,
            RuntimeAddress = pixels.RuntimeAddress,
            Format = pixels.Format,
            LevelCount = pixels.LevelCount,
            DimensionCount = pixels.DimensionCount,
            MultiFaceControl = pixels.MultiFaceControl,
            TextureControl1 = pixels.TextureControl1,
            Width = pixels.Width,
            Height = pixels.Height,
            Depth = pixels.Depth,
            MemoryLocation = pixels.SerializedMemoryLocation,
            MinLodControl = pixels.MinLodControl,
            RenderTargetPitch = pixels.RenderTargetPitch,
            PixelsOffset = pixels.SerializedPixelsOffset,
            MapType = pixels.MapType,
            TextureSemantic = _state.Semantic,
            Category = pixels.Category,
            UseSrgbReads = _state.UseSrgbReads,
            CardMemory = pixels.CardMemory,
            BaseWidth = pixels.BaseWidth,
            BaseHeight = pixels.BaseHeight,
            BaseDepth = pixels.BaseDepth,
            BaseLevelCount = pixels.BaseLevelCount,
            Cached = pixels.Cached,
            PayloadPointer = pixels.PayloadPointer,
            StreamData = Array.AsReadOnly(pixels.StreamData.ToArray()),
            StreamImageIndex = pixels.StreamImageIndex,
            StreamEntries = Array.AsReadOnly(pixels.StreamEntries.ToArray()),
            PayloadByteCount = pixels.PayloadByteCount,
            PayloadBytes = pixels.PayloadBytes is byte[] bytes
                ? Array.AsReadOnly(bytes)
                : pixels.PayloadBytes,
            NamePointer = pixels.NamePointer,
            Name = pixels.Name
        };
    }

    private void RequireEditableMetadata()
    {
        if (!CanEditMetadata)
            throw new InvalidOperationException("Metadata editing is unavailable for this image.");
    }

    private static bool IsEmptyImage(GfxImageAsset image) =>
        image.Category == ImageCategory.Unknown && image.MapType == MapType.None &&
        image.Width == 0 && image.Height == 0 && image.Depth == 0 && image.LevelCount == 0 &&
        image.PayloadPointer.Raw == 0 && image.PayloadBytes.Count == 0 &&
        !image.StreamData.Any(part => part.HasStreamingData);

    internal static ImageFileShape? GetShape(GfxImageAsset image) => image.MapType switch
    {
        MapType.TwoDimensional when image.DimensionCount == GfxImageDimension.TwoDimensional &&
            !image.IsCubemap && image.Depth == 1 => ImageFileShape.TwoDimensional,
        MapType.Cube when image.DimensionCount == GfxImageDimension.TwoDimensional &&
            image.IsCubemap && image.Depth == 1 && image.Width == image.Height => ImageFileShape.Cube,
        MapType.ThreeDimensional when image.DimensionCount == GfxImageDimension.ThreeDimensional &&
            !image.IsCubemap && image.Depth > 0 => ImageFileShape.Volume,
        _ => null
    };

    private readonly record struct ImageState(GfxImageAsset Pixels, TextureSemantic Semantic, byte UseSrgbReads);
}

internal sealed class ImageAdapter : AssetAuthoringAdapter<GfxImageAsset, ImageDraft>
{
    public override XAssetType AssetType => XAssetType.Image;
    public override ImageDraft CreateDraft(GfxImageAsset definition) => new(definition);
    public override ImageDraft CloneDraft(ImageDraft draft) => draft.Clone();
    public override GfxImageAsset CreateDefinition(ImageDraft draft) => draft.CreateDefinition();
    public override bool SemanticallyEquals(ImageDraft left, ImageDraft right) => left.SemanticallyEquals(right);
}
