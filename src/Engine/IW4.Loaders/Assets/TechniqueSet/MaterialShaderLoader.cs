using IW4.Loaders.Database;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.TechniqueSet;

/// <summary>
/// Reproduces the shared PS3 pointer-wrapper and body shapes for the
/// MaterialPixelShader and MaterialVertexShader XAsset families.
/// </summary>
public sealed class MaterialShaderLoader
{
    private static readonly MaterialShaderAssetLoader PixelLoader = new(MaterialShaderKind.Pixel);
    private static readonly MaterialShaderAssetLoader VertexLoader = new(MaterialShaderKind.Vertex);

    public MaterialShaderAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, kind, context, requireAsset: true)
            ?? throw new InvalidDataException($"Top-level {MaterialShaderAssetLoader.GetDisplayName(kind)} pointer resolved to null.");
    }

    public MaterialShaderAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, kind, context, requireAsset: false);
    }

    private static MaterialShaderAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        MaterialShaderAssetLoader loader = kind switch
        {
            MaterialShaderKind.Pixel => PixelLoader,
            MaterialShaderKind.Vertex => VertexLoader,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        // The native pointer wrappers push stream 0 before resolving null,
        // packed, inline, or insert-pointer cases. Nested calls can originate
        // in LARGE while their shader roots remain TEMP staging allocations.
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            return requireAsset
                ? loader.LoadFromAssetPointer(cursor, pointer, context)
                : loader.LoadFromPointer(cursor, pointer, context);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }
}
