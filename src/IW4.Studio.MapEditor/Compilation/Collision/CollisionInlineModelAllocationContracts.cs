using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.Collision;

public enum CollisionInlineModelOwnerKind
{
    World = 0,
    MapEntityBrushModel = 1,
    DynamicBrushDefinition = 2
}

/// <summary>
/// One MapEnt-owned inline brush model. Physical entity order is the
/// corpus-proven allocation order; the stable object ID remains the semantic
/// identity used by editor commands.
/// </summary>
public sealed record MapEntInlineModelAllocationSource
{
    public MapEntInlineModelAllocationSource(
        MapObjectId objectId,
        int physicalEntityOrdinal)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (physicalEntityOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalEntityOrdinal));
        }
        ObjectId = objectId;
        PhysicalEntityOrdinal = physicalEntityOrdinal;
    }

    public MapObjectId ObjectId { get; }
    public int PhysicalEntityOrdinal { get; }
}

/// <summary>
/// One slot-1 dynamic brush-model owner. PhysicsBrushModel is a separate
/// namespace. The M0 corpus proves only zero; nonzero values remain
/// fail-closed until a separate physics-model allocation authority exists and
/// are never copied from the shared render/collision model ordinal.
/// </summary>
public sealed record DynamicBrushInlineModelAllocationSource
{
    public DynamicBrushInlineModelAllocationSource(
        MapObjectId objectId,
        int sourceOrder,
        ushort physicsBrushModelOrdinal = 0)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (sourceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrder));
        if (physicsBrushModelOrdinal != 0)
        {
            throw new ArgumentException(
                "Nonzero PhysicsBrushModel is not authorable until its " +
                "independent physics-model domain has a typed allocation " +
                "authority.",
                nameof(physicsBrushModelOrdinal));
        }

        ObjectId = objectId;
        SourceOrder = sourceOrder;
        PhysicsBrushModelOrdinal = physicsBrushModelOrdinal;
    }

    public MapObjectId ObjectId { get; }
    public int SourceOrder { get; }
    public ushort PhysicsBrushModelOrdinal { get; }
}

public sealed record CollisionImportedMapEntInlineModelOwner
{
    public CollisionImportedMapEntInlineModelOwner(
        MapObjectId objectId,
        int physicalEntityOrdinal,
        ushort modelOrdinal)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (physicalEntityOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalEntityOrdinal));
        }
        if (modelOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelOrdinal),
                "An imported MapEnt brush model cannot claim world row 0.");
        }

        ObjectId = objectId;
        PhysicalEntityOrdinal = physicalEntityOrdinal;
        ModelOrdinal = modelOrdinal;
    }

    public MapObjectId ObjectId { get; }
    public int PhysicalEntityOrdinal { get; }
    public ushort ModelOrdinal { get; }
}

public sealed record CollisionImportedDynamicBrushInlineModelOwner
{
    public CollisionImportedDynamicBrushInlineModelOwner(
        MapObjectId objectId,
        int sourceOrder,
        ushort dynamicDefinitionOrdinal,
        ushort modelOrdinal,
        ushort physicsBrushModelOrdinal = 0)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (sourceOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrder));
        if (modelOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelOrdinal),
                "An imported dynamic brush model cannot claim world row 0.");
        }
        if (physicsBrushModelOrdinal != 0)
        {
            throw new ArgumentException(
                "The imported PhysicsBrushModel namespace is unresolved; " +
                "the M0 corpus authorizes only zero.",
                nameof(physicsBrushModelOrdinal));
        }

        ObjectId = objectId;
        SourceOrder = sourceOrder;
        DynamicDefinitionOrdinal = dynamicDefinitionOrdinal;
        ModelOrdinal = modelOrdinal;
        PhysicsBrushModelOrdinal = physicsBrushModelOrdinal;
    }

    public MapObjectId ObjectId { get; }
    public int SourceOrder { get; }
    public ushort DynamicDefinitionOrdinal { get; }
    public ushort ModelOrdinal { get; }
    public ushort PhysicsBrushModelOrdinal { get; }
}

/// <summary>
/// Imported owner inventory for the shared Gfx/Col model tables and slot-1
/// dynamic-definition table. Construction validates the exact corpus-proven
/// world / MapEnt prefix / dynamic suffix order rather than accepting an
/// arbitrary dense permutation.
/// </summary>
public sealed class CollisionImportedInlineModelDomain
{
    private readonly IReadOnlyList<
        CollisionImportedMapEntInlineModelOwner> _mapEntityOwners;
    private readonly IReadOnlyList<
        CollisionImportedDynamicBrushInlineModelOwner> _dynamicOwners;

    public CollisionImportedInlineModelDomain(
        int colMapModelCount,
        int gfxMapModelCount,
        IEnumerable<CollisionImportedMapEntInlineModelOwner> mapEntityOwners,
        IEnumerable<CollisionImportedDynamicBrushInlineModelOwner>
            dynamicOwners)
    {
        if (colMapModelCount <= 0 ||
            colMapModelCount > ushort.MaxValue + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colMapModelCount));
        }
        if (gfxMapModelCount <= 0 ||
            gfxMapModelCount > ushort.MaxValue + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gfxMapModelCount));
        }
        ArgumentNullException.ThrowIfNull(mapEntityOwners);
        ArgumentNullException.ThrowIfNull(dynamicOwners);

        CollisionImportedMapEntInlineModelOwner[] mapEntityCopy =
            mapEntityOwners.ToArray();
        CollisionImportedDynamicBrushInlineModelOwner[] dynamicCopy =
            dynamicOwners.ToArray();
        if (mapEntityCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Imported MapEnt owner inventory cannot contain null.",
                nameof(mapEntityOwners));
        }
        if (dynamicCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Imported dynamic owner inventory cannot contain null.",
                nameof(dynamicOwners));
        }
        CollisionImportedMapEntInlineModelOwner[] mapEntities =
            mapEntityCopy
                .OrderBy(value => value.PhysicalEntityOrdinal)
                .ToArray();
        CollisionImportedDynamicBrushInlineModelOwner[] dynamics =
            dynamicCopy
                .OrderBy(value => value.SourceOrder)
                .ToArray();

        MapObjectId? duplicateOwner = mapEntities
            .Select(value => value.ObjectId)
            .Concat(dynamics.Select(value => value.ObjectId))
            .GroupBy(value => value)
            .FirstOrDefault(value => value.Count() != 1)
            ?.Key;
        if (duplicateOwner is not null)
        {
            throw new InvalidDataException(
                $"Imported inline-model owner {duplicateOwner} is " +
                "classified more than once.");
        }
        RejectDuplicateOrder(
            mapEntities.Select(value => value.PhysicalEntityOrdinal),
            "imported MapEnt physical entity");
        RejectDuplicateOrder(
            dynamics.Select(value => value.SourceOrder),
            "imported dynamic brush source");

        int expectedModelCount =
            checked(1 + mapEntities.Length + dynamics.Length);
        if (colMapModelCount != gfxMapModelCount ||
            colMapModelCount != expectedModelCount)
        {
            throw new InvalidDataException(
                "Imported inline-model counts must match and equal one " +
                "world row plus every classified MapEnt and dynamic owner; " +
                $"expected {expectedModelCount}, ColMap has " +
                $"{colMapModelCount}, GfxMap has {gfxMapModelCount}.");
        }
        for (int index = 0; index < mapEntities.Length; index++)
        {
            int expectedModelOrdinal = 1 + index;
            if (mapEntities[index].ModelOrdinal != expectedModelOrdinal)
            {
                throw new InvalidDataException(
                    "Imported MapEnt inline-model ownership is not the " +
                    "corpus-proven physical-order prefix; expected model " +
                    $"{expectedModelOrdinal}, found " +
                    $"{mapEntities[index].ModelOrdinal}.");
            }
        }
        int dynamicModelStart = 1 + mapEntities.Length;
        for (int index = 0; index < dynamics.Length; index++)
        {
            if (dynamics[index].DynamicDefinitionOrdinal != index ||
                dynamics[index].ModelOrdinal != dynamicModelStart + index)
            {
                throw new InvalidDataException(
                    "Imported dynamic brush ownership is not the " +
                    "corpus-proven definition-order model suffix; expected " +
                    $"definition {index}, model {dynamicModelStart + index}, " +
                    $"found definition " +
                    $"{dynamics[index].DynamicDefinitionOrdinal}, model " +
                    $"{dynamics[index].ModelOrdinal}.");
            }
        }

        ColMapModelCount = colMapModelCount;
        GfxMapModelCount = gfxMapModelCount;
        _mapEntityOwners = Array.AsReadOnly(mapEntities);
        _dynamicOwners = Array.AsReadOnly(dynamics);
    }

    public int ColMapModelCount { get; }
    public int GfxMapModelCount { get; }
    public int DynamicBrushDefinitionCount => _dynamicOwners.Count;

    public IReadOnlyList<CollisionImportedMapEntInlineModelOwner>
        MapEntityOwners => _mapEntityOwners;

    public IReadOnlyList<CollisionImportedDynamicBrushInlineModelOwner>
        DynamicOwners => _dynamicOwners;

    private static void RejectDuplicateOrder(
        IEnumerable<int> orders,
        string description)
    {
        int? duplicate = orders
            .GroupBy(value => value)
            .FirstOrDefault(value => value.Count() != 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Two owners claim {description} order {duplicate}.");
        }
    }
}

public enum CollisionImportedOrdinalDispositionKind
{
    Retained = 0,
    Removed = 1
}

public readonly record struct CollisionImportedOrdinalDisposition
{
    public CollisionImportedOrdinalDisposition(
        ushort importedOrdinal,
        MapObjectId? ownerObjectId,
        CollisionImportedOrdinalDispositionKind kind,
        ushort? emittedOrdinal)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if ((kind == CollisionImportedOrdinalDispositionKind.Retained) !=
            emittedOrdinal.HasValue)
        {
            throw new ArgumentException(
                "A retained imported ordinal requires one emitted ordinal; " +
                "a removed ordinal must not carry one.",
                nameof(emittedOrdinal));
        }

        ImportedOrdinal = importedOrdinal;
        OwnerObjectId = ownerObjectId;
        Kind = kind;
        EmittedOrdinal = emittedOrdinal;
    }

    public ushort ImportedOrdinal { get; }
    public MapObjectId? OwnerObjectId { get; }
    public CollisionImportedOrdinalDispositionKind Kind { get; }
    public ushort? EmittedOrdinal { get; }
}

public sealed record CollisionInlineModelAllocation(
    CollisionInlineModelOwnerKind OwnerKind,
    MapObjectId? OwnerObjectId,
    int? OwnerSourceOrder,
    ushort ModelOrdinal,
    ushort? DynamicDefinitionOrdinal,
    ushort PhysicsBrushModelOrdinal);

/// <summary>
/// Immutable shared Gfx/Col inline-model allocation. The only legal authored
/// order is world row 0, MapEnt owners in physical entity order, then slot-1
/// dynamic brush owners in their deterministic source order.
/// </summary>
public sealed class CollisionInlineModelAllocationPlan
{
    private readonly IReadOnlyList<CollisionInlineModelAllocation> _rows;
    private readonly IReadOnlyDictionary<
        MapObjectId,
        CollisionInlineModelAllocation> _byOwner;
    private readonly IReadOnlyDictionary<ushort, ushort> _modelOldToNew;
    private readonly IReadOnlyDictionary<ushort, ushort>
        _dynamicDefinitionOldToNew;
    private readonly IReadOnlyList<CollisionImportedOrdinalDisposition>
        _modelDispositions;
    private readonly IReadOnlyList<CollisionImportedOrdinalDisposition>
        _dynamicDefinitionDispositions;

    private CollisionInlineModelAllocationPlan(
        IReadOnlyList<CollisionInlineModelAllocation> rows,
        IReadOnlyDictionary<
            MapObjectId,
            CollisionInlineModelAllocation> byOwner,
        IReadOnlyDictionary<ushort, ushort> modelOldToNew,
        IReadOnlyDictionary<ushort, ushort> dynamicDefinitionOldToNew,
        IReadOnlyList<CollisionImportedOrdinalDisposition> modelDispositions,
        IReadOnlyList<CollisionImportedOrdinalDisposition>
            dynamicDefinitionDispositions)
    {
        _rows = rows;
        _byOwner = byOwner;
        _modelOldToNew = modelOldToNew;
        _dynamicDefinitionOldToNew = dynamicDefinitionOldToNew;
        _modelDispositions = modelDispositions;
        _dynamicDefinitionDispositions = dynamicDefinitionDispositions;
    }

    public IReadOnlyList<CollisionInlineModelAllocation> Rows => _rows;
    public int ModelCount => _rows.Count;

    public IReadOnlyDictionary<ushort, ushort> ImportedModelOldToNew =>
        _modelOldToNew;

    public IReadOnlyDictionary<ushort, ushort>
        ImportedDynamicDefinitionOldToNew =>
        _dynamicDefinitionOldToNew;

    public IReadOnlyList<CollisionImportedOrdinalDisposition>
        ImportedModelDispositions => _modelDispositions;

    public IReadOnlyList<CollisionImportedOrdinalDisposition>
        ImportedDynamicDefinitionDispositions =>
        _dynamicDefinitionDispositions;

    public static CollisionInlineModelAllocationPlan Create(
        IEnumerable<MapEntInlineModelAllocationSource> mapEntitySources,
        IEnumerable<DynamicBrushInlineModelAllocationSource> dynamicSources,
        CollisionImportedInlineModelDomain? importedDomain = null)
    {
        ArgumentNullException.ThrowIfNull(mapEntitySources);
        ArgumentNullException.ThrowIfNull(dynamicSources);

        MapEntInlineModelAllocationSource[] mapEntityCopy =
            mapEntitySources.ToArray();
        DynamicBrushInlineModelAllocationSource[] dynamicCopy =
            dynamicSources.ToArray();
        if (mapEntityCopy.Any(value => value is null))
            throw new ArgumentException(
                "MapEnt inline-model sources cannot contain null entries.",
                nameof(mapEntitySources));
        if (dynamicCopy.Any(value => value is null))
            throw new ArgumentException(
                "Dynamic inline-model sources cannot contain null entries.",
                nameof(dynamicSources));
        MapEntInlineModelAllocationSource[] mapEntities =
            mapEntityCopy
                .OrderBy(value => value.PhysicalEntityOrdinal)
                .ThenBy(
                    value => value.ObjectId.Value.ToString("N"),
                    StringComparer.Ordinal)
                .ToArray();
        DynamicBrushInlineModelAllocationSource[] dynamics =
            dynamicCopy
                .OrderBy(value => value.SourceOrder)
                .ThenBy(
                    value => value.ObjectId.Value.ToString("N"),
                    StringComparer.Ordinal)
                .ToArray();

        MapObjectId? duplicateOwner = mapEntities
            .Select(value => value.ObjectId)
            .Concat(dynamics.Select(value => value.ObjectId))
            .GroupBy(value => value)
            .FirstOrDefault(value => value.Count() != 1)
            ?.Key;
        if (duplicateOwner is not null)
        {
            throw new ArgumentException(
                $"Inline-model owner {duplicateOwner} is classified more " +
                "than once.");
        }
        RejectDuplicateOrder(
            mapEntities.Select(value => value.PhysicalEntityOrdinal),
            "MapEnt physical entity");
        RejectDuplicateOrder(
            dynamics.Select(value => value.SourceOrder),
            "dynamic brush source");

        int modelCount = checked(1 + mapEntities.Length + dynamics.Length);
        if (modelCount > ushort.MaxValue + 1)
        {
            throw new OverflowException(
                $"Inline-model allocation requires {modelCount} rows, but " +
                "the shared consumers carry unsigned-16 ordinals.");
        }
        if (dynamics.Length > ushort.MaxValue)
        {
            throw new OverflowException(
                "DynEntDefList[1] cannot contain more than 65,535 rows.");
        }

        var rows = new List<CollisionInlineModelAllocation>(modelCount)
        {
            new(
                CollisionInlineModelOwnerKind.World,
                OwnerObjectId: null,
                OwnerSourceOrder: null,
                ModelOrdinal: 0,
                DynamicDefinitionOrdinal: null,
                PhysicsBrushModelOrdinal: 0)
        };
        var byOwner =
            new Dictionary<MapObjectId, CollisionInlineModelAllocation>(
                modelCount - 1);

        foreach (MapEntInlineModelAllocationSource source in mapEntities)
        {
            ushort modelOrdinal = checked((ushort)rows.Count);
            var allocation = new CollisionInlineModelAllocation(
                CollisionInlineModelOwnerKind.MapEntityBrushModel,
                source.ObjectId,
                source.PhysicalEntityOrdinal,
                modelOrdinal,
                DynamicDefinitionOrdinal: null,
                PhysicsBrushModelOrdinal: 0);
            rows.Add(allocation);
            byOwner.Add(source.ObjectId, allocation);
        }

        for (int index = 0; index < dynamics.Length; index++)
        {
            DynamicBrushInlineModelAllocationSource source = dynamics[index];
            ushort dynamicDefinitionOrdinal = checked((ushort)index);
            ushort modelOrdinal = checked((ushort)rows.Count);
            var allocation = new CollisionInlineModelAllocation(
                CollisionInlineModelOwnerKind.DynamicBrushDefinition,
                source.ObjectId,
                source.SourceOrder,
                modelOrdinal,
                dynamicDefinitionOrdinal,
                source.PhysicsBrushModelOrdinal);
            rows.Add(allocation);
            byOwner.Add(source.ObjectId, allocation);
        }

        (
            Dictionary<ushort, ushort> modelOldToNew,
            Dictionary<ushort, ushort> dynamicOldToNew,
            CollisionImportedOrdinalDisposition[] modelDispositions,
            CollisionImportedOrdinalDisposition[]
                dynamicDefinitionDispositions) =
            BuildImportedDispositions(
            importedDomain,
            byOwner);

        return new CollisionInlineModelAllocationPlan(
            Array.AsReadOnly(rows.ToArray()),
            new ReadOnlyDictionary<
                MapObjectId,
                CollisionInlineModelAllocation>(byOwner),
            new ReadOnlyDictionary<ushort, ushort>(modelOldToNew),
            new ReadOnlyDictionary<ushort, ushort>(dynamicOldToNew),
            Array.AsReadOnly(modelDispositions),
            Array.AsReadOnly(dynamicDefinitionDispositions));
    }

    public CollisionInlineModelAllocation GetRequiredOwner(
        MapObjectId ownerObjectId)
    {
        if (ownerObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(ownerObjectId));

        return _byOwner.TryGetValue(ownerObjectId, out var allocation)
            ? allocation
            : throw new KeyNotFoundException(
                $"No inline-model allocation exists for {ownerObjectId}.");
    }

    public CollisionInlineModelIdentityInput CreateIdentityInput()
    {
        MapEntInlineModelBinding[] mapEntModels = _rows
            .Where(value =>
                value.OwnerKind ==
                CollisionInlineModelOwnerKind.MapEntityBrushModel)
            .Select(value =>
                new MapEntInlineModelBinding(
                    value.OwnerSourceOrder!.Value,
                    value.ModelOrdinal))
            .ToArray();
        DynamicBrushModelBinding[] dynamicBindings = _rows
            .Where(value =>
                value.OwnerKind ==
                CollisionInlineModelOwnerKind.DynamicBrushDefinition)
            .Select(value =>
                new DynamicBrushModelBinding(
                    value.DynamicDefinitionOrdinal!.Value,
                    value.ModelOrdinal,
                    value.PhysicsBrushModelOrdinal))
            .ToArray();

        return new CollisionInlineModelIdentityInput(
            ModelCount,
            ModelCount,
            mapEntModels,
            dynamicBindings);
    }

    private static void RejectDuplicateOrder(
        IEnumerable<int> orders,
        string description)
    {
        int? duplicate = orders
            .GroupBy(value => value)
            .FirstOrDefault(value => value.Count() != 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Two inline-model owners claim {description} order " +
                $"{duplicate}.");
        }
    }

    private static (
        Dictionary<ushort, ushort> ModelOldToNew,
        Dictionary<ushort, ushort> DynamicDefinitionOldToNew,
        CollisionImportedOrdinalDisposition[] ModelDispositions,
        CollisionImportedOrdinalDisposition[] DynamicDefinitionDispositions)
        BuildImportedDispositions(
        CollisionImportedInlineModelDomain? importedDomain,
        IReadOnlyDictionary<
            MapObjectId,
            CollisionInlineModelAllocation> currentByOwner)
    {
        var modelOldToNew = new Dictionary<ushort, ushort>();
        var dynamicOldToNew = new Dictionary<ushort, ushort>();
        if (importedDomain is null)
        {
            return (
                modelOldToNew,
                dynamicOldToNew,
                [],
                []);
        }

        var modelDispositions =
            new List<CollisionImportedOrdinalDisposition>(
                importedDomain.ColMapModelCount)
        {
            Retained(
                importedOrdinal: 0,
                ownerObjectId: null,
                emittedOrdinal: 0)
        };
        modelOldToNew.Add(0, 0);

        foreach (CollisionImportedMapEntInlineModelOwner imported in
                 importedDomain.MapEntityOwners)
        {
            CollisionImportedOrdinalDisposition disposition =
                ResolveImported(
                    imported.ModelOrdinal,
                    imported.ObjectId,
                    CollisionInlineModelOwnerKind.MapEntityBrushModel,
                    currentByOwner);
            modelDispositions.Add(disposition);
            AddRetainedMapping(modelOldToNew, disposition);
        }

        var dynamicDefinitionDispositions =
            new List<CollisionImportedOrdinalDisposition>(
                importedDomain.DynamicBrushDefinitionCount);
        foreach (CollisionImportedDynamicBrushInlineModelOwner imported in
                 importedDomain.DynamicOwners)
        {
            CollisionImportedOrdinalDisposition modelDisposition =
                ResolveImported(
                    imported.ModelOrdinal,
                    imported.ObjectId,
                    CollisionInlineModelOwnerKind.DynamicBrushDefinition,
                    currentByOwner);
            modelDispositions.Add(modelDisposition);
            AddRetainedMapping(modelOldToNew, modelDisposition);

            CollisionImportedOrdinalDisposition definitionDisposition;
            if (modelDisposition.Kind ==
                CollisionImportedOrdinalDispositionKind.Removed)
            {
                definitionDisposition = Removed(
                    imported.DynamicDefinitionOrdinal,
                    imported.ObjectId);
            }
            else
            {
                CollisionInlineModelAllocation current =
                    currentByOwner[imported.ObjectId];
                definitionDisposition = Retained(
                    imported.DynamicDefinitionOrdinal,
                    imported.ObjectId,
                    current.DynamicDefinitionOrdinal!.Value);
            }
            dynamicDefinitionDispositions.Add(definitionDisposition);
            AddRetainedMapping(
                dynamicOldToNew,
                definitionDisposition);
        }

        if (modelDispositions.Count !=
            importedDomain.ColMapModelCount ||
            dynamicDefinitionDispositions.Count !=
            importedDomain.DynamicBrushDefinitionCount)
        {
            throw new InvalidDataException(
                "Imported inline-model owner inventory did not produce one " +
                "explicit disposition for every old row.");
        }

        return (
            modelOldToNew,
            dynamicOldToNew,
            modelDispositions.ToArray(),
            dynamicDefinitionDispositions.ToArray());
    }

    private static CollisionImportedOrdinalDisposition ResolveImported(
        ushort importedOrdinal,
        MapObjectId ownerObjectId,
        CollisionInlineModelOwnerKind expectedKind,
        IReadOnlyDictionary<
            MapObjectId,
            CollisionInlineModelAllocation> currentByOwner)
    {
        if (!currentByOwner.TryGetValue(ownerObjectId, out var current))
            return Removed(importedOrdinal, ownerObjectId);
        if (current.OwnerKind != expectedKind)
        {
            throw new InvalidDataException(
                $"Imported inline-model owner {ownerObjectId} changed from " +
                $"{expectedKind} to {current.OwnerKind}; ownership-kind " +
                "migration has no M0 contract.");
        }

        return Retained(
            importedOrdinal,
            ownerObjectId,
            current.ModelOrdinal);
    }

    private static void AddRetainedMapping(
        IDictionary<ushort, ushort> mapping,
        CollisionImportedOrdinalDisposition disposition)
    {
        if (disposition.Kind ==
            CollisionImportedOrdinalDispositionKind.Removed)
        {
            return;
        }
        mapping.Add(
            disposition.ImportedOrdinal,
            disposition.EmittedOrdinal!.Value);
    }

    private static CollisionImportedOrdinalDisposition Retained(
        ushort importedOrdinal,
        MapObjectId? ownerObjectId,
        ushort emittedOrdinal) =>
        new(
            importedOrdinal,
            ownerObjectId,
            CollisionImportedOrdinalDispositionKind.Retained,
            emittedOrdinal);

    private static CollisionImportedOrdinalDisposition Removed(
        ushort importedOrdinal,
        MapObjectId ownerObjectId) =>
        new(
            importedOrdinal,
            ownerObjectId,
            CollisionImportedOrdinalDispositionKind.Removed,
            emittedOrdinal: null);
}
