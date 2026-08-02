using System.Buffers.Binary;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Process-global allocator for the 15-bit technique-state IDs stored in
/// material runtime data.
/// </summary>
public sealed class MaterialTechniqueStateCache
{
    private const int HashSlotCount = 0x8000;
    private const int MaxRegisteredStateCount = 0x6000;
    private const int InlineStateIdOffset = 0x44;
    private const int RuntimeStateIdPointerOffset = 0x90;

    private MaterialTechniqueStateOwner?[] _ownersByHashSlot = new MaterialTechniqueStateOwner?[HashSlotCount];
    private MaterialTechniqueStateCacheTransaction? _activeTransaction;

    public int Count { get; private set; }

    public MaterialTechniqueStateCacheTransaction BeginTransaction()
    {
        if (_activeTransaction is not null)
        {
            throw new InvalidOperationException(
                "The material technique-state cache already has an active load transaction.");
        }

        var transaction = new MaterialTechniqueStateCacheTransaction(this, CaptureState());
        _activeTransaction = transaction;
        return transaction;
    }

    internal void ApplyNewProvider(
        MaterialAsset material,
        XAssetPool assetPool,
        XAssetPoolEntry entry)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(assetPool);
        ArgumentNullException.ThrowIfNull(entry);
        if (!ReferenceEquals(entry.Asset, material))
            throw new InvalidOperationException("Material state IDs can only be assigned to the active incoming provider.");
        if (entry.HeaderBytes.Length < MaterialAsset.SerializedSize)
            throw new InvalidDataException("Canonical Material pool copy is shorter than 0xA8 bytes.");

        var resolver = new MaterialGraphResolver(assetPool);
        if (resolver.GetTechniqueSet(material) is null)
        {
            throw new InvalidDataException(
                $"Material '{material.Info.Name}' has no resolved technique set for state assignment.");
        }

        MaterialTechniqueStateDescription[] descriptions = BuildDescriptions(material, resolver);
        var owner = new MaterialTechniqueStateOwner(Array.AsReadOnly(descriptions));
        foreach (MaterialTechniqueStateDescription description in descriptions)
        {
            ushort stateId = Register(owner, description);
            material.SetTechniqueSlotStateId(description.TechniqueSlot, description.PassIndex, stateId);
            if (description.PassIndex == 0)
                WriteInlineStateId(entry, description.TechniqueSlot, stateId);
            else
                WriteRuntimeStateId(entry, description.TechniqueSlot, stateId);
        }
    }

    internal static ushort FoldHashToSlot(uint hash) =>
        checked((ushort)(unchecked(hash + (hash >> 16)) & 0x7fff));

    internal void CommitTransaction(MaterialTechniqueStateCacheTransaction transaction)
    {
        EnsureActiveTransaction(transaction);
        _activeTransaction = null;
    }

    internal void RollbackTransaction(
        MaterialTechniqueStateCacheTransaction transaction,
        MaterialTechniqueStateCacheState state)
    {
        EnsureActiveTransaction(transaction);
        RestoreState(state);
        _activeTransaction = null;
    }

    private static MaterialTechniqueStateDescription[] BuildDescriptions(
        MaterialAsset material,
        MaterialGraphResolver resolver)
    {
        var descriptions = new List<MaterialTechniqueStateDescription>();
        for (int slotIndex = 0; slotIndex < MaterialAsset.TechniqueSlotCount; slotIndex++)
        {
            MaterialTechniqueAsset? technique = resolver.GetTechnique(material, slotIndex);
            if (technique is null)
                continue;
            if (technique.Passes.Count != technique.PassCount || technique.PassCount == 0)
            {
                throw new InvalidDataException(
                    $"Material '{material.Info.Name}' technique slot {slotIndex} declares {technique.PassCount} " +
                    $"pass(es), but {technique.Passes.Count} are materialized.");
            }

            descriptions.Add(MaterialTechniqueStateKeyBuilder.Build(
                material,
                technique.Passes[0],
                slotIndex,
                passIndex: 0));
            if (technique.PassCount == 2)
            {
                descriptions.Add(MaterialTechniqueStateKeyBuilder.Build(
                    material,
                    technique.Passes[1],
                    slotIndex,
                    passIndex: 1));
            }
        }

        return descriptions.ToArray();
    }

    private ushort Register(
        MaterialTechniqueStateOwner owner,
        MaterialTechniqueStateDescription incoming)
    {
        ushort slot = FoldHashToSlot(incoming.Hash);
        while (true)
        {
            MaterialTechniqueStateOwner? existingOwner = _ownersByHashSlot[slot];
            if (existingOwner is null)
            {
                if (Count >= MaxRegisteredStateCount)
                {
                    throw new InvalidDataException(
                        "Material technique-state cache exceeded its 0x6000-entry capacity.");
                }

                _ownersByHashSlot[slot] = owner;
                Count++;
                return slot;
            }

            if (existingOwner.Descriptions.Any(
                    candidate => candidate.Hash == incoming.Hash &&
                                 AreSemanticallyEquivalent(candidate, incoming)))
            {
                return slot;
            }

            slot = checked((ushort)((slot + 1) & 0x7fff));
        }
    }

    private static bool AreSemanticallyEquivalent(
        MaterialTechniqueStateDescription first,
        MaterialTechniqueStateDescription second)
    {
        if (!first.CodePixelConstantIndices.SequenceEqual(second.CodePixelConstantIndices) ||
            first.PixelConstants.Count != second.PixelConstants.Count)
        {
            return false;
        }

        for (int index = 0; index < first.PixelConstants.Count; index++)
        {
            ResolvedConstant left = first.PixelConstants[index];
            ResolvedConstant right = second.PixelConstants[index];
            if (left.Destination != right.Destination ||
                !NativeFloatEquals(left.Value.X, right.Value.X) ||
                !NativeFloatEquals(left.Value.Y, right.Value.Y) ||
                !NativeFloatEquals(left.Value.Z, right.Value.Z) ||
                !NativeFloatEquals(left.Value.W, right.Value.W))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NativeFloatEquals(float first, float second) =>
        !(first < second) && !(second < first);

    private static void WriteInlineStateId(
        XAssetPoolEntry entry,
        int techniqueSlot,
        ushort stateId)
    {
        int offset = InlineStateIdOffset + techniqueSlot * sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(entry.HeaderBytes.AsSpan(offset, sizeof(ushort)), stateId);
        if (!ReferenceEquals(entry.HeaderBytes, entry.NativePoolCopyBytes))
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                entry.NativePoolCopyBytes.AsSpan(offset, sizeof(ushort)),
                stateId);
        }
    }

    private static void WriteRuntimeStateId(
        XAssetPoolEntry entry,
        int techniqueSlot,
        ushort stateId)
    {
        IXAssetSourceMemory blocks = entry.SourceBlocks
            ?? throw new InvalidDataException(
                $"Material '{entry.Name}' has no source block state for its second-pass technique-state table.");
        int pointerRaw = BinaryPrimitives.ReadInt32BigEndian(
            entry.HeaderBytes.AsSpan(RuntimeStateIdPointerOffset, sizeof(int)));
        if (!XPointerCodec.TryDecodeBlockAddress(pointerRaw, out XBlockAddress tableAddress))
        {
            throw new InvalidDataException(
                $"Material '{entry.Name}' has a two-pass technique but +0x90 is not a materialized runtime table pointer " +
                $"(0x{unchecked((uint)pointerRaw):X8}).");
        }

        blocks.WriteUInt16(tableAddress.Add(techniqueSlot * sizeof(ushort)), stateId);
    }

    internal MaterialTechniqueStateCacheState CaptureState() =>
        new((MaterialTechniqueStateOwner?[])_ownersByHashSlot.Clone(), Count);

    internal void RestoreState(MaterialTechniqueStateCacheState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ownersByHashSlot = (MaterialTechniqueStateOwner?[])state.OwnersByHashSlot.Clone();
        Count = state.Count;
    }

    private void EnsureActiveTransaction(MaterialTechniqueStateCacheTransaction transaction)
    {
        if (!ReferenceEquals(_activeTransaction, transaction))
            throw new InvalidOperationException("Material technique-state cache transaction is no longer active.");
    }
}
