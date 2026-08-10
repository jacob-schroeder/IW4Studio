using System.Collections.ObjectModel;
using IW4.FastFiles.Zone;

namespace IW4.Linker.Model;

public sealed class ZoneObjectFile
{
    internal ZoneObjectFile(
        byte[] decodedTape,
        XFile declaredLayout,
        IEnumerable<AllocationEvent> allocationLedger,
        IEnumerable<TempLifetime> tempLifetimes,
        IEnumerable<AllocationSymbol> allocations,
        IEnumerable<LocalAssetProviderSymbol> localProviders,
        IEnumerable<ImportedAssetProviderSymbol> importedProviders,
        IEnumerable<AssetProviderSelectionEvent> providerSelections,
        IEnumerable<XStringSymbol> strings,
        IEnumerable<AliasCellSymbol> aliases,
        IEnumerable<BoundarySymbol> boundaries,
        IEnumerable<PointerRelocation> relocations)
    {
        DecodedTape = Array.AsReadOnly(decodedTape.ToArray());
        DeclaredLayout = new XFile(declaredLayout.Size, declaredLayout.ExternalSize, declaredLayout.BlockSizes);
        AllocationLedger = Array.AsReadOnly(allocationLedger.ToArray());
        TempLifetimes = Array.AsReadOnly(tempLifetimes.ToArray());
        Allocations = Array.AsReadOnly(allocations.ToArray());
        LocalAssetProviders = Array.AsReadOnly(localProviders.ToArray());
        ImportedAssetProviders = Array.AsReadOnly(importedProviders.ToArray());
        ProviderSelections = Array.AsReadOnly(providerSelections.ToArray());
        XStrings = Array.AsReadOnly(strings.ToArray());
        AliasCells = Array.AsReadOnly(aliases.ToArray());
        Boundaries = Array.AsReadOnly(boundaries.ToArray());
        Relocations = Array.AsReadOnly(relocations.ToArray());
        ValidateSymbolOccurrences();
    }

    public ReadOnlyCollection<byte> DecodedTape { get; }
    public XFile DeclaredLayout { get; }
    public ReadOnlyCollection<AllocationEvent> AllocationLedger { get; }
    public ReadOnlyCollection<TempLifetime> TempLifetimes { get; }
    public ReadOnlyCollection<AllocationSymbol> Allocations { get; }
    public ReadOnlyCollection<LocalAssetProviderSymbol> LocalAssetProviders { get; }
    public ReadOnlyCollection<ImportedAssetProviderSymbol> ImportedAssetProviders { get; }
    public ReadOnlyCollection<AssetProviderSelectionEvent> ProviderSelections { get; }
    public ReadOnlyCollection<XStringSymbol> XStrings { get; }
    public ReadOnlyCollection<AliasCellSymbol> AliasCells { get; }
    public ReadOnlyCollection<BoundarySymbol> Boundaries { get; }
    public ReadOnlyCollection<PointerRelocation> Relocations { get; }

    private void ValidateSymbolOccurrences()
    {
        var occurrences = new HashSet<CaptureOccurrence>();
        IEnumerable<ZoneSymbol> symbols = Allocations.Cast<ZoneSymbol>()
            .Concat(LocalAssetProviders)
            .Concat(ImportedAssetProviders)
            .Concat(XStrings)
            .Concat(AliasCells)
            .Concat(Boundaries);
        foreach (ZoneSymbol symbol in symbols)
        {
            if (symbol.Occurrence.Value <= 0 || !occurrences.Add(symbol.Occurrence))
                throw new InvalidDataException("Zone object contains duplicate or invalid symbol occurrence identities.");
        }
    }
}
