using IW4.FastFiles.Zone;

namespace IW4.Linker.Plans;

public abstract class ZoneSymbol
{
    internal ZoneSymbol(CaptureOccurrence occurrence) => Occurrence = occurrence;
    public CaptureOccurrence Occurrence { get; }
}

public sealed class AllocationSymbol : ZoneSymbol
{
    internal AllocationSymbol(AllocationEvent allocation) : base(allocation.Occurrence) => Allocation = allocation;
    public AllocationEvent Allocation { get; }
}

/// <summary>A provider that is materialized by this zone object.</summary>
public abstract class AssetProviderSymbol : ZoneSymbol
{
    internal AssetProviderSymbol(CaptureOccurrence occurrence) : base(occurrence) { }
}

public sealed class LocalAssetProviderSymbol : AssetProviderSymbol
{
    internal LocalAssetProviderSymbol(
        CaptureOccurrence occurrence,
        AllocationReference providerCell,
        AllocationSymbol materialization)
        : base(occurrence)
    {
        ProviderCell = providerCell;
        Materialization = materialization;
    }

    /// <summary>The durable serialized cell used to publish this provider.</summary>
    public AllocationReference ProviderCell { get; }
    public AllocationSymbol Materialization { get; }
}

/// <summary>
/// A provider selected from outside this object. Its identity is graph-local
/// and opaque; loader runtime ids never escape into the frozen object file.
/// </summary>
public sealed class ImportedAssetProviderSymbol : AssetProviderSymbol
{
    internal ImportedAssetProviderSymbol(CaptureOccurrence occurrence) : base(occurrence) { }
}

/// <summary>One immutable incoming-provider to selected-provider edge.</summary>
public sealed record AssetProviderSelectionEvent(
    CaptureOccurrence Occurrence,
    LocalAssetProviderSymbol Incoming,
    AssetProviderSymbol ActiveProvider);

public sealed class XStringSymbol : ZoneSymbol
{
    internal XStringSymbol(CaptureOccurrence occurrence, AllocationSymbol allocation) : base(occurrence) => Allocation = allocation;
    public AllocationSymbol Allocation { get; }
}

public sealed class AliasCellSymbol : ZoneSymbol
{
    internal AliasCellSymbol(CaptureOccurrence occurrence, AllocationSymbol allocation, int addend)
        : base(occurrence)
    {
        Allocation = allocation;
        Addend = addend;
    }

    public AllocationSymbol Allocation { get; }
    public int Addend { get; }
}

/// <summary>
/// An occurrence-scoped, addressable zero-byte view. It records a validated
/// boundary without pretending that the boundary materialized a byte.
/// </summary>
public sealed class BoundarySymbol : ZoneSymbol
{
    internal BoundarySymbol(BoundaryEvent boundary) : base(boundary.Occurrence) => Boundary = boundary;
    public BoundaryEvent Boundary { get; }
}
