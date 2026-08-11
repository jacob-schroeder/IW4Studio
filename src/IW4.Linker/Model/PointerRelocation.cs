using IW4.FastFiles.Pointers;

namespace IW4.Linker.Model;

public enum SerializedPointerForm { Null, Inline, Insert, PackedDirect, PackedAlias }
public enum SerializedByteOrder { BigEndian }

public abstract record SymbolReference(int Addend, bool AllowsEndAddress = false);
public sealed record AllocationReference(AllocationSymbol Symbol, int Addend = 0, bool AllowsEndAddress = false)
    : SymbolReference(Addend, AllowsEndAddress);
public sealed record AssetProviderReference(AssetProviderSymbol Symbol, int Addend = 0)
    : SymbolReference(Addend);
public sealed record XStringReference(XStringSymbol Symbol, int Addend = 0)
    : SymbolReference(Addend);
public sealed record AliasCellReference(AliasCellSymbol Symbol, int Addend = 0)
    : SymbolReference(Addend);
public sealed record BoundaryReference(BoundarySymbol Symbol) : SymbolReference(0);

/// <summary>
/// One source pointer word, its concrete source owner, and its symbolic target.
/// AmbientTempEpoch is the TEMP lifetime active when the word was read; the
/// source allocation lifetime is separate because a parent allocation can be
/// patched while a nested TEMP body is active.
/// </summary>
public sealed record PointerRelocation(
    CaptureOccurrence Occurrence,
    int TapeOffset,
    int Width,
    SerializedByteOrder ByteOrder,
    int CapturedRaw,
    SerializedPointerForm Form,
    XPointerResolutionMode ResolutionMode,
    AllocationReference? Source,
    long AmbientTempEpoch,
    long? SourceAllocationTempEpoch,
    long? TargetTempEpoch,
    SymbolReference? Target,
    AliasCellReference? PublicationCell);
