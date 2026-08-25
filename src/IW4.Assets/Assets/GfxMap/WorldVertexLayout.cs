using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// PS3 backend world-vertex source table. Event20 selects row 4 by default and
/// row 5 + worldVertexFormat when the selected MaterialTechnique declares an
/// optional source.
/// </summary>
internal static class WorldVertexLayout
{
    internal const int WorldVertexStride = 0x10;
    internal const int DefaultBackendRow =
        (int)MaterialVertexDeclarationType.WorldPositionOnly;
    internal const int WorldFormatBackendRowBase =
        (int)MaterialVertexDeclarationType.WorldTex1Nrm1;
    internal const int SourceTableRowSize = 0x26;

    // Rows 0..16 cover every serialized MaterialWorldVertexFormat value used
    // by Event20.
    private static readonly byte[][] SourceRows =
    [
        Row("2000000004020010040400140202001C01060200000002000000020000000200000002000000"),
        Row("200000000402001004040014020300180106001C010602000000020000000200000002000000"),
        Row("101000000402010004040104020301080106010C010602000000020000000200000002000000"),
        Row("2C00000004020010040400140402002401060028010602000000020000000200000002000000"),
        Row("1000000004020200000002000000020000000200000002000000020000000200000002000000"),
        Row("101C000004020100040401040402011401060118010602000000020000000200000002000000"),
        Row("10240000040201000404010404020114010601180106011C0202020000000200000002000000"),
        Row("10280000040201000404010404020114010601180106011C0202020000000124040402000000"),
        Row("102C0000040201000404010404020114010601180106011C0402020000000200000002000000"),
        Row("10300000040201000404010404020114010601180106011C040202000000012C040402000000"),
        Row("10340000040201000404010404020114010601180106011C040202000000012C040401300404"),
        Row("10340000040201000404010404020114010601180106011C0402012C02020200000002000000"),
        Row("10380000040201000404010404020114010601180106011C0402012C02020134040402000000"),
        Row("103C0000040201000404010404020114010601180106011C0402012C02020134040401380404"),
        Row("103C0000040201000404010404020114010601180106011C0402012C04020200000002000000"),
        Row("10400000040201000404010404020114010601180106011C0402012C0402013C040402000000"),
        Row("10440000040201000404010404020114010601180106011C0402012C0402013C040401400404")
    ];

    static WorldVertexLayout()
    {
        if (SourceRows.Length != (int)MaterialVertexDeclarationType.Count)
        {
            throw new InvalidDataException(
                "PS3 world-vertex source table has an unexpected row count.");
        }
        if (SourceRows.Any(row => row.Length != SourceTableRowSize))
            throw new InvalidDataException("PS3 world-vertex source table contains a malformed row.");
    }

    internal static int BackendRowCount => SourceRows.Length;

    internal static int ResolveEffectiveBackendRow(
        MaterialTechniqueFlags techniqueFlags,
        MaterialWorldVertexFormat worldVertexFormat)
    {
        return (techniqueFlags &
                MaterialTechniqueFlags.DeclarationHasOptionalSource) != 0
            ? WorldFormatBackendRowBase + (int)worldVertexFormat
            : DefaultBackendRow;
    }

    internal static int ResolveGenericFallbackBackendRow(MaterialWorldVertexFormat worldVertexFormat) =>
        WorldFormatBackendRowBase + (int)worldVertexFormat;

    internal static bool HasBackendRow(int backendRow) =>
        (uint)backendRow < (uint)SourceRows.Length;

    internal static bool TryGetSource(
        int backendRow,
        MaterialStreamSource source,
        out WorldVertexSource vertexSource)
    {
        int sourceOffset = 2 + (int)source * 4;
        if (!HasBackendRow(backendRow) ||
            (uint)(sourceOffset + 3) >= (uint)SourceRows[backendRow].Length)
        {
            vertexSource = default;
            return false;
        }

        byte[] row = SourceRows[backendRow];
        vertexSource = new WorldVertexSource(
            row[sourceOffset],
            row[sourceOffset + 1],
            row[sourceOffset + 2],
            (RsxVertexElementType)row[sourceOffset + 3]);
        return true;
    }

    internal static bool TryGetStreamStride(
        int backendRow,
        byte streamIndex,
        out byte stride)
    {
        if (!HasBackendRow(backendRow) || streamIndex >= 2)
        {
            stride = 0;
            return false;
        }

        stride = SourceRows[backendRow][streamIndex];
        return stride != 0;
    }

    /// <summary>
    /// Event20 world-stream selection. Row 4 is the one-stream default; rows
    /// 5..16 are the twelve flagged world-format rows and bind their second
    /// row stride as vertex-layer data.
    /// </summary>
    internal static bool TryGetEvent20StreamStrides(
        int backendRow,
        out byte worldVertexStride,
        out byte vertexLayerStride)
    {
        bool isDefaultRow = backendRow == DefaultBackendRow;
        bool isFlaggedWorldRow = backendRow >= WorldFormatBackendRowBase &&
            backendRow < SourceRows.Length;
        if (!isDefaultRow && !isFlaggedWorldRow)
        {
            worldVertexStride = 0;
            vertexLayerStride = 0;
            return false;
        }

        worldVertexStride = SourceRows[backendRow][0];
        vertexLayerStride = isDefaultRow ? (byte)0 : SourceRows[backendRow][1];
        return worldVertexStride == WorldVertexStride &&
            (isDefaultRow || vertexLayerStride != 0);
    }

    internal static byte GetRowByte(int backendRow, byte rowOffset)
    {
        if (!HasBackendRow(backendRow) || rowOffset >= SourceTableRowSize)
            return 0;

        return SourceRows[backendRow][rowOffset];
    }

    private static byte[] Row(string hex) => Convert.FromHexString(hex);
}
