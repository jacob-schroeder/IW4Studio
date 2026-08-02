using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using AssetBounds = IW4.Assets.Math.Bounds;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Detached collision-side static-model row and its symbolic XModel
/// dependency identity. Dependency realization remains M7 linker work.
/// </summary>
public sealed record CollisionCompiledStaticModelLocalPayload(
    MapObjectId SourceObjectId,
    MapObjectId RenderCounterpartObjectId,
    string ExactSerializedModelName,
    ClipStaticModel StaticModel,
    MapBounds Bounds);

public sealed class CollisionCompiledStaticModelAggregatePayload
{
    internal CollisionCompiledStaticModelAggregatePayload(
        IEnumerable<CollisionCompiledStaticModelLocalPayload> sources,
        IEnumerable<ClipStaticModel> staticModels,
        IEnumerable<SModelAabbNode> aabbNodes)
    {
        Sources = ReadOnly(sources);
        StaticModels = ReadOnly(staticModels);
        AabbNodes = ReadOnly(aabbNodes);
    }

    public IReadOnlyList<CollisionCompiledStaticModelLocalPayload> Sources
    {
        get;
    }
    public IReadOnlyList<ClipStaticModel> StaticModels { get; }
    public IReadOnlyList<SModelAabbNode> AabbNodes { get; }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        T[] copy = source.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Compiled static-model payloads cannot contain null rows.",
                nameof(source));
        }
        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// Deterministic M3 compiler for collision-side static-model placement. A
/// single conservative AABB root references the dense static-model range.
/// This is an isolated structural candidate; XModel linking and runtime
/// consumer acceptance remain outside this compiler.
/// </summary>
public static class CollisionStaticModelPayloadCompiler
{
    public const string AabbPolicyIdentity =
        "iw4-studio.colmap.static-model-single-root@2";

    public static CollisionCompiledStaticModelAggregatePayload Compile(
        IEnumerable<AuthoredPairedStaticModelCollisionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        AuthoredPairedStaticModelCollisionSource[] ordered = sources
            .OrderBy(
                value => value.ObjectId.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();
        MapObjectId? duplicate = ordered
            .GroupBy(value => value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Paired static-model collision source {duplicate} is " +
                "duplicated.",
                nameof(sources));
        }
        MapObjectId? duplicateCounterpart = ordered
            .GroupBy(value =>
                value.Ownership.Counterpart!.Value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateCounterpart is not null)
        {
            throw new ArgumentException(
                $"Render static-model counterpart {duplicateCounterpart} " +
                "is claimed more than once.",
                nameof(sources));
        }
        if (ordered.Length > ushort.MaxValue)
        {
            throw new OverflowException(
                "The single-root static-model AABB policy supports at most " +
                "65,535 collision static models.");
        }

        var local =
            new CollisionCompiledStaticModelLocalPayload[ordered.Length];
        var rows = new ClipStaticModel[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            AuthoredPairedStaticModelCollisionSource source = ordered[index];
            if (source.Ownership.Category !=
                CollisionOwnershipCategory.PairedStaticModel)
            {
                throw new ArgumentException(
                    $"Static-model source {source.ObjectId} does not have " +
                    "explicit paired ownership.",
                    nameof(sources));
            }

            ClipStaticModel row = CompileLocal(source);
            rows[index] = row;
            local[index] = new CollisionCompiledStaticModelLocalPayload(
                source.ObjectId,
                source.Ownership.Counterpart!.Value.ObjectId,
                source.ExactSerializedModelName,
                row,
                source.Bounds);
        }

        SModelAabbNode[] nodes =
        [
            new SModelAabbNode
            {
                Bounds = rows.Length == 0
                    ? new AssetBounds()
                    : CollisionOutwardBounds.ToAsset(
                        CollisionOutwardBounds.Include(
                            local.Select(value => value.Bounds))),
                FirstChild = 0,
                ChildCount = checked((ushort)rows.Length)
            }
        ];
        return new CollisionCompiledStaticModelAggregatePayload(
            local,
            rows,
            nodes);
    }

    private static ClipStaticModel CompileLocal(
        AuthoredPairedStaticModelCollisionSource source) =>
        new()
        {
            // The symbolic exact name above is the dependency identity.
            // Runtime XModel objects and source pointers never enter M3.
            XModel = null,
            Origin = ToAsset(source.Origin),
            InvScaledAxis = Array.AsReadOnly(
                source.InverseScaledAxis.Select(ToAsset).ToArray()),
            // Despite the legacy field names, IW4 consumers treat these as
            // serialized midpoint and half-size.
            AbsMin = ToAsset(source.Bounds.MidPoint),
            AbsMax = ToAsset(source.Bounds.HalfSize)
        };

    private static AssetVector3 ToAsset(MapVector3 value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };
}
