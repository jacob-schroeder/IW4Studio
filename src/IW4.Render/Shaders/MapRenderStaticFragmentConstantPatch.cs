namespace IW4.Render.Shaders;

/// <summary>
/// Records one stable material/literal constant resolved by the translator
/// pixel argument and applied its value to a valid fragment patch-table entry.
/// </summary>
public sealed record MapRenderStaticFragmentConstantPatch
{
    public MapRenderStaticFragmentConstantPatch(
        int argumentOrdinal,
        MapRenderSelectedPassConstantKind kind,
        ushort destination,
        int argumentRaw,
        MapRenderShaderConstantValue value,
        int patchSiteCount)
    {
        ArgumentOrdinal = argumentOrdinal;
        Kind = kind;
        Destination = destination;
        ArgumentRaw = argumentRaw;
        Value = value;
        PatchSiteCount = patchSiteCount;
    }

    public int ArgumentOrdinal { get; }

    public MapRenderSelectedPassConstantKind Kind { get; }

    public ushort Destination { get; }

    public int ArgumentRaw { get; }

    public MapRenderShaderConstantValue Value { get; }

    public int PatchSiteCount { get; }
}
