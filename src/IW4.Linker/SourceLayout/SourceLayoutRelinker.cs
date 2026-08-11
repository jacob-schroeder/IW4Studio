using System.Buffers.Binary;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Plans;

namespace IW4.Linker.SourceLayout;

public sealed record SourceLayoutRelinkError(string Code, string Message);

/// <summary>A failed source-layout replay never exposes a partially patched byte tape.</summary>
public sealed class SourceLayoutRelinkResult
{
    private SourceLayoutRelinkResult(
        byte[]? decodedBytes,
        IEnumerable<SourceLayoutRelinkError> errors)
    {
        DecodedBytes = decodedBytes is null ? null : decodedBytes.ToArray();
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public bool Succeeded => DecodedBytes is not null;
    public ReadOnlyMemory<byte>? DecodedBytes { get; }
    public IReadOnlyList<SourceLayoutRelinkError> Errors { get; }
    internal static SourceLayoutRelinkResult Success(byte[] bytes) => new(bytes, []);
    internal static SourceLayoutRelinkResult Failure(
        IEnumerable<SourceLayoutRelinkError> errors) => new(null, errors);
}

/// <summary>
/// Deterministically replays an unchanged, frozen object file in its captured
/// source layout. This is not a canonical asset link.
/// </summary>
public sealed class SourceLayoutRelinker
{
    public SourceLayoutRelinkResult Relink(ZoneObjectFile objectFile)
    {
        ArgumentNullException.ThrowIfNull(objectFile);
        try
        {
            Validate(objectFile);
            byte[] output = objectFile.DecodedTape.ToArray();
            foreach (PointerRelocation relocation in objectFile.Relocations)
            {
                int encoded = Encode(relocation);
                if (encoded != relocation.CapturedRaw)
                {
                    return SourceLayoutRelinkResult.Failure([new(
                        "sourceRelink.sourceCompatibility",
                        $"Relocation at 0x{relocation.TapeOffset:X} re-encodes " +
                        $"0x{unchecked((uint)encoded):X8}, not captured source " +
                        $"word 0x{unchecked((uint)relocation.CapturedRaw):X8}.")]);
                }
                BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(relocation.TapeOffset, relocation.Width), encoded);
            }
            return SourceLayoutRelinkResult.Success(output);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            return SourceLayoutRelinkResult.Failure([new("sourceRelink.validation", exception.Message)]);
        }
    }

    private static void Validate(ZoneObjectFile objectFile)
    {
        if (objectFile.DeclaredLayout.BlockSizes.Count != XFile.BlockCount)
            throw new InvalidDataException("Zone object has invalid XFile block extents.");
        byte[] tape = objectFile.DecodedTape.ToArray();
        var lifetimes = objectFile.TempLifetimes.ToDictionary(value => value.Epoch);
        foreach (BoundarySymbol boundary in objectFile.Boundaries)
        {
            BoundaryEvent placement = boundary.Boundary;
            int block = (int)placement.DestinationBlock;
            if (boundary.Occurrence != placement.Occurrence || block < 0 ||
                block >= objectFile.DeclaredLayout.BlockSizes.Count || placement.DestinationOffset < 0 ||
                placement.DestinationOffset > objectFile.DeclaredLayout.BlockSizes[block])
            {
                throw new InvalidDataException("Zone object contains an invalid boundary symbol placement.");
            }
            if (!lifetimes.TryGetValue(placement.TempEpoch, out TempLifetime? lifetime) ||
                !Contains(lifetime, placement.Occurrence.Value))
            {
                throw new InvalidDataException("Zone object contains a boundary symbol outside its TEMP lifetime.");
            }
        }
        var seen = new HashSet<int>();
        var relocationOccurrences = new HashSet<CaptureOccurrence>();
        foreach (PointerRelocation relocation in objectFile.Relocations)
        {
            if (relocation.Occurrence.Value <= 0 || !relocationOccurrences.Add(relocation.Occurrence))
                throw new InvalidDataException("Zone object contains duplicate or invalid relocation occurrence identities.");
            if (relocation.Width != sizeof(int) || relocation.ByteOrder != SerializedByteOrder.BigEndian)
                throw new InvalidDataException("Zone object contains a relocation encoding unsupported by the PS3 fastfile linker.");
            if (relocation.TapeOffset < 0 || relocation.TapeOffset > tape.Length - relocation.Width || !seen.Add(relocation.TapeOffset))
                throw new InvalidDataException("Zone object contains duplicate or out-of-range relocation tape cells.");
            if (BinaryPrimitives.ReadInt32BigEndian(tape.AsSpan(relocation.TapeOffset, relocation.Width)) != relocation.CapturedRaw)
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} no longer matches its captured tape word.");
            if (relocation.Form != SerializedPointerForm.Null && relocation.Target is null)
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has no target symbol.");
            if (relocation.Form == SerializedPointerForm.Null && relocation.Target is not null)
                throw new InvalidDataException($"Null relocation at 0x{relocation.TapeOffset:X} unexpectedly has a target symbol.");
            if (relocation.AmbientTempEpoch <= 0)
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has an invalid ambient TEMP lifetime.");
            if (relocation.Source is null != (relocation.SourceAllocationTempEpoch is null))
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has an incomplete source allocation lifetime.");
            if (relocation.Source is { } sourceTarget &&
                sourceTarget.Symbol.Allocation.TempEpoch != relocation.SourceAllocationTempEpoch)
            {
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has a mismatched source allocation lifetime.");
            }
            if (relocation.Target is null != (relocation.TargetTempEpoch is null))
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has an incomplete target lifetime.");
            if (relocation.Target is not null && relocation.TargetTempEpoch <= 0)
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has an invalid target lifetime.");
            if (relocation.Target is { } capturedTarget &&
                TargetLifetime(capturedTarget) != relocation.TargetTempEpoch)
            {
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} has a mismatched target lifetime.");
            }
            if (relocation.Form == SerializedPointerForm.PackedAlias &&
                relocation.ResolutionMode != XPointerResolutionMode.AliasCell)
            {
                throw new InvalidDataException($"Packed alias relocation at 0x{relocation.TapeOffset:X} has the wrong resolution mode.");
            }
            if (relocation.Form == SerializedPointerForm.PackedDirect &&
                relocation.ResolutionMode == XPointerResolutionMode.AliasCell)
            {
                throw new InvalidDataException($"Packed direct relocation at 0x{relocation.TapeOffset:X} has the wrong resolution mode.");
            }
            if (relocation.PublicationCell is not null &&
                (relocation.ResolutionMode != XPointerResolutionMode.AliasCell ||
                 relocation.Form is not (SerializedPointerForm.Inline or SerializedPointerForm.Insert)))
            {
                throw new InvalidDataException(
                    $"Relocation at 0x{relocation.TapeOffset:X} has an invalid alias publication cell.");
            }
            if (!lifetimes.TryGetValue(relocation.AmbientTempEpoch, out TempLifetime? ambientLifetime) ||
                !Contains(ambientLifetime, relocation.Occurrence.Value))
            {
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} was captured outside its ambient TEMP lifetime.");
            }
            if (relocation.SourceAllocationTempEpoch is { } sourceEpoch &&
                (!lifetimes.TryGetValue(sourceEpoch, out TempLifetime? sourceLifetime) ||
                 !Contains(sourceLifetime, relocation.Occurrence.Value)))
            {
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} references a source allocation outside its TEMP lifetime.");
            }
            if (relocation.Form is SerializedPointerForm.PackedDirect or SerializedPointerForm.PackedAlias &&
                relocation.TargetTempEpoch is { } targetEpoch &&
                (!lifetimes.TryGetValue(targetEpoch, out TempLifetime? targetLifetime) ||
                 !Contains(targetLifetime, relocation.Occurrence.Value)))
            {
                throw new InvalidDataException($"Relocation at 0x{relocation.TapeOffset:X} targets a future or retired TEMP lifetime.");
            }
            if (relocation.Source is { } source)
                _ = Address(source.Symbol.Allocation, source.Addend, source.AllowsEndAddress);
            if (relocation.Form is SerializedPointerForm.PackedDirect or SerializedPointerForm.PackedAlias)
                _ = EncodeAddress(relocation.Target);
            if (relocation.PublicationCell is { } publicationCell)
                _ = EncodeAddress(publicationCell);
        }
    }

    private static int Encode(PointerRelocation relocation) => relocation.Form switch
    {
        SerializedPointerForm.Null => 0,
        SerializedPointerForm.Inline => -1,
        SerializedPointerForm.Insert => -2,
        SerializedPointerForm.PackedDirect => EncodeAddress(relocation.Target),
        SerializedPointerForm.PackedAlias => EncodeAddress(relocation.Target),
        _ => throw new InvalidDataException("Unknown pointer form.")
    };

    private static int EncodeAddress(SymbolReference? target)
    {
        return target switch
        {
            AllocationReference reference => XPointerCodec.Encode(Address(reference.Symbol.Allocation, reference.Addend, reference.AllowsEndAddress)),
            XStringReference reference => XPointerCodec.Encode(Address(reference.Symbol.Allocation.Allocation, reference.Addend, false)),
            AliasCellReference reference => XPointerCodec.Encode(Address(reference.Symbol.Allocation.Allocation, checked(reference.Symbol.Addend + reference.Addend), false)),
            BoundaryReference reference => XPointerCodec.Encode(Address(reference.Symbol.Boundary)),
            // Providers are published through their durable cell. The body is
            // intentionally not an addressable provider identity.
            AssetProviderReference { Symbol: LocalAssetProviderSymbol local } reference => XPointerCodec.Encode(Address(
                local.ProviderCell.Symbol.Allocation,
                checked(local.ProviderCell.Addend + reference.Addend),
                false)),
            AssetProviderReference => throw new InvalidDataException(
                "A packed relocation cannot encode an external provider without an import-link policy."),
            _ => throw new InvalidDataException("A packed pointer does not reference an encodable symbol.")
        };
    }

    private static XBlockAddress Address(AllocationEvent allocation, int addend, bool allowsEndAddress)
    {
        // A zero-length allocation has one legal coordinate: its explicit
        // occurrence start. Capture only emits that case through an exact
        // owner/materialization relationship, never address lookup.
        if (addend < 0 ||
            (allocation.Length == 0
                ? addend != 0
                : allowsEndAddress ? addend > allocation.Length : addend >= allocation.Length))
            throw new InvalidDataException("Symbol addend lies outside its allocation.");
        return new XBlockAddress(allocation.DestinationBlock, checked(allocation.DestinationOffset + addend));
    }

    private static XBlockAddress Address(BoundaryEvent boundary)
    {
        if (boundary.Occurrence.Value <= 0 || boundary.DestinationOffset < 0 || boundary.TempEpoch <= 0)
            throw new InvalidDataException("Boundary symbol has invalid placement data.");
        return new XBlockAddress(boundary.DestinationBlock, boundary.DestinationOffset);
    }

    private static long TargetLifetime(SymbolReference target) => target switch
    {
        AllocationReference reference => reference.Symbol.Allocation.TempEpoch,
        AssetProviderReference { Symbol: LocalAssetProviderSymbol local } => local.ProviderCell.Symbol.Allocation.TempEpoch,
        AssetProviderReference => 1,
        XStringReference reference => reference.Symbol.Allocation.Allocation.TempEpoch,
        AliasCellReference reference => reference.Symbol.Allocation.Allocation.TempEpoch,
        BoundaryReference reference => reference.Symbol.Boundary.TempEpoch,
        _ => throw new InvalidDataException("Unknown symbolic pointer target.")
    };

    private static bool Contains(TempLifetime lifetime, long sequence) =>
        lifetime.BeginSequence <= sequence && sequence < lifetime.EndSequence;
}
