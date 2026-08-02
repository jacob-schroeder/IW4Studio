namespace IW4.Render.Scheduling;

/// <summary>
/// Managed view of the PS3 0x0C-byte header and its 13-by-7 technique table.
/// </summary>
public sealed class MapRenderDrawMethod
{
    public const int NativeHeaderSize = 0x0C;

    private readonly byte[] _techniqueTable;

    internal MapRenderDrawMethod(
        MapRenderDrawSceneMode drawScene,
        int baseTechnique,
        int emissiveTechnique,
        byte[] techniqueTable)
    {
        ArgumentNullException.ThrowIfNull(techniqueTable);
        if (techniqueTable.Length !=
            MapRenderDrawMethodPageProducer.TechniqueTableLength)
        {
            throw new ArgumentException(
                "Invalid draw-method technique-table length.",
                nameof(techniqueTable));
        }

        DrawScene = drawScene;
        BaseTechnique = baseTechnique;
        EmissiveTechnique = emissiveTechnique;
        _techniqueTable = techniqueTable.ToArray();
        TechniqueTable = Array.AsReadOnly(_techniqueTable);
    }

    public MapRenderDrawSceneMode DrawScene { get; }

    public int BaseTechnique { get; }

    public int EmissiveTechnique { get; }

    public IReadOnlyList<byte> TechniqueTable { get; }

    internal ReadOnlySpan<byte> TechniqueTableSpan => _techniqueTable;

    public ReadOnlySpan<byte> GetTechniqueRow(
        MapRenderSurfaceType surfaceType)
    {
        int pageIndex = (int)surfaceType;
        if ((uint)pageIndex >= MapRenderDrawMethodPageProducer.PageCount)
            throw new ArgumentOutOfRangeException(nameof(surfaceType));

        return _techniqueTable.AsSpan(
            pageIndex * MapRenderDrawMethodPageProducer.VariantCount,
            MapRenderDrawMethodPageProducer.VariantCount);
    }
}
