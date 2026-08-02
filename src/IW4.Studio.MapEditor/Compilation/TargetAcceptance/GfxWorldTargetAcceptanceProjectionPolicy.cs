namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Separates the original serializer-shape probe from renderer-safe no-bake
/// defaults. The serializer-only form intentionally remains byte-compatible
/// with the M4 managed boundary.
/// </summary>
internal enum GfxWorldLightingProjectionContract
{
    SerializerOnlyUnclassified = 0,
    TargetRuntimeNoBake = 1
}

/// <summary>
/// Internal projection parameters for the managed target probes. Keeping the
/// policy typed prevents serializer-only placeholder ranges from being reused
/// as an implicit lighting compiler.
/// </summary>
internal sealed class GfxWorldTargetAcceptanceProjectionPolicy
{
    private GfxWorldTargetAcceptanceProjectionPolicy(
        uint litSurfacesBegin,
        uint litSurfacesEnd,
        IEnumerable<uint> surfaceCategoryEndpoints,
        int sunPrimaryLightIndex,
        int primaryLightCount,
        byte fogTypesAllowed,
        GfxWorldLightingProjectionContract lightingContract)
    {
        ArgumentNullException.ThrowIfNull(surfaceCategoryEndpoints);
        if (!Enum.IsDefined(lightingContract))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lightingContract));
        }
        uint[] endpoints = surfaceCategoryEndpoints.ToArray();
        if (endpoints.Length != 6)
        {
            throw new ArgumentException(
                "A GfxWorld surface-classification policy requires exactly " +
                "six ordered category endpoints.",
                nameof(surfaceCategoryEndpoints));
        }
        if (litSurfacesBegin > litSurfacesEnd ||
            endpoints.Prepend(litSurfacesEnd)
                .Zip(endpoints)
                .Any(pair => pair.First > pair.Second))
        {
            throw new ArgumentException(
                "GfxWorld surface partitions must be monotonically ordered.",
                nameof(surfaceCategoryEndpoints));
        }
        if (sunPrimaryLightIndex < 0 ||
            primaryLightCount < 0 ||
            sunPrimaryLightIndex > primaryLightCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sunPrimaryLightIndex));
        }

        LitSurfacesBegin = litSurfacesBegin;
        LitSurfacesEnd = litSurfacesEnd;
        SurfaceCategoryEndpoints = Array.AsReadOnly(endpoints);
        SunPrimaryLightIndex = sunPrimaryLightIndex;
        PrimaryLightCount = primaryLightCount;
        FogTypesAllowed = fogTypesAllowed;
        LightingContract = lightingContract;
    }

    public uint LitSurfacesBegin { get; }
    public uint LitSurfacesEnd { get; }
    public IReadOnlyList<uint> SurfaceCategoryEndpoints { get; }
    public int SunPrimaryLightIndex { get; }
    public int PrimaryLightCount { get; }
    public byte FogTypesAllowed { get; }
    public GfxWorldLightingProjectionContract LightingContract { get; }

    internal static GfxWorldTargetAcceptanceProjectionPolicy
        SerializerOnlyUnclassified { get; } =
        new(
            litSurfacesBegin: 0,
            litSurfacesEnd: 0,
            surfaceCategoryEndpoints: [0, 0, 0, 0, 0, 0],
            sunPrimaryLightIndex: 0,
            primaryLightCount: 0,
            fogTypesAllowed: 0,
            lightingContract:
                GfxWorldLightingProjectionContract
                    .SerializerOnlyUnclassified);

    internal static GfxWorldTargetAcceptanceProjectionPolicy
        NoBakeAllOpaque(int surfaceCount)
    {
        if (surfaceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceCount),
                "The bounded no-bake profile requires visible geometry.");
        }

        uint endpoint = checked((uint)surfaceCount);
        return new(
            litSurfacesBegin: 0,
            litSurfacesEnd: endpoint,
            surfaceCategoryEndpoints:
                [endpoint, endpoint, endpoint, endpoint, endpoint, endpoint],
            sunPrimaryLightIndex: 0,
            primaryLightCount: 1,
            // This selects the deterministic non-sun-direction draw-method
            // family. It does not claim that upstream fog is disabled.
            fogTypesAllowed: 1,
            lightingContract:
                GfxWorldLightingProjectionContract
                    .TargetRuntimeNoBake);
    }
}
