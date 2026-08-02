using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using AssetBounds = IW4.Assets.Math.Bounds;
using AssetVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Stable, fail-closed reasons why a compiled static-model translation cannot
/// preserve its imported IW4 lighting assignment.
/// </summary>
public enum StaticModelLightingPreservationIssueKind
{
    InvalidStaticModelTable,
    InvalidDestinationOrigin,
    GroundLightingIsBaked,
    InvalidReflectionProbeState,
    ReflectionProbeSourceCalibrationFailed,
    ReflectionProbeDestinationWouldChange,
    InvalidPrimaryLightState,
    UnsupportedNonSunPrimaryLight,
    UnsupportedSpotLightAssociation,
    NonSunPrimaryLightAssociation,
    InvalidLightGridState,
    SourceLightGridSampleUnavailable,
    DestinationLightGridSampleUnavailable,
    SourceLightGridSampleNeedsTrace,
    DestinationLightGridSampleNeedsTrace
}

public sealed record StaticModelLightingPreservationIssue(
    StaticModelLightingPreservationIssueKind Kind,
    string Detail);

/// <summary>
/// Immutable proof material that a translation may copy the imported lighting
/// tuple without changing its meaning. The eventual authoring operation must
/// preserve all five serialized fields exactly.
/// </summary>
public sealed record StaticModelLightingPreservationEvidence(
    int StaticModelOrdinal,
    Float3BuildData SourceOrigin,
    Float3BuildData DestinationOrigin,
    Float3BuildData SourceBoundsMidPoint,
    Float3BuildData DestinationBoundsMidPoint,
    Float3BuildData SourceLightingOrigin,
    Float3BuildData DestinationLightingOrigin,
    ushort LightingHandle,
    byte ReflectionProbeIndex,
    byte PrimaryLightIndex,
    byte Flags,
    GfxColor GroundLighting,
    IReadOnlyList<int> SourceLightGridEntryIndices,
    IReadOnlyList<int> DestinationLightGridEntryIndices);

public sealed class StaticModelLightingPreservationEligibility
{
    internal StaticModelLightingPreservationEligibility(
        IEnumerable<StaticModelLightingPreservationIssue> issues,
        StaticModelLightingPreservationEvidence? evidence)
    {
        StaticModelLightingPreservationIssue[] snapshot = issues.ToArray();
        Issues = Array.AsReadOnly(snapshot);
        Evidence = snapshot.Length == 0 ? evidence : null;
    }

    public bool IsEligible => Issues.Count == 0 && Evidence is not null;
    public IReadOnlyList<StaticModelLightingPreservationIssue> Issues { get; }
    public StaticModelLightingPreservationEvidence? Evidence { get; }
    public string EvidenceSummary =>
        IsEligible
            ? "Imported static-model lighting handle, reflection probe, " +
              "primary light, flags, and ground color remain byte-identical; " +
              "both translated light-grid samples are valid and require no " +
              "collision trace."
            : string.Join(" ", Issues.Select(issue => issue.Detail));
}

/// <summary>
/// Evaluates the narrow translation-only path proven from the IW4 PS3
/// consumers. It does not rebuild lighting. It only proves that the imported
/// handle/probe/primary/flags/ground tuple may remain byte-identical while
/// light-grid colors are resampled at the translated lighting origin.
/// </summary>
public static class StaticModelLightingPreservationEligibilityEvaluator
{
    private const byte GroundLightingFlag = 0x02;
    private const float MinimumCornerWeight = 0.001f;
    private const float GridOrigin = -131072f;

    public static StaticModelLightingPreservationEligibility Evaluate(
        GfxWorldBuildData gfxWorld,
        ComWorldBuildData comWorld,
        int staticModelOrdinal,
        Float3BuildData destinationOrigin)
    {
        ArgumentNullException.ThrowIfNull(gfxWorld);
        ArgumentNullException.ThrowIfNull(comWorld);

        var issues = new List<StaticModelLightingPreservationIssue>();
        GfxWorldAsset world = gfxWorld.Definition;
        if (!TryGetStaticModel(
                world,
                staticModelOrdinal,
                out GfxStaticModelDrawInst? draw,
                out GfxStaticModelInst? instance,
                out string? staticModelFailure))
        {
            return Failed(
                StaticModelLightingPreservationIssueKind.InvalidStaticModelTable,
                staticModelFailure!);
        }
        if (!IsFinite(destinationOrigin))
        {
            return Failed(
                StaticModelLightingPreservationIssueKind.InvalidDestinationOrigin,
                "The absolute destination origin contains a non-finite component.");
        }

        Float3BuildData sourceOrigin = ToPoint(draw!.Placement.Origin);
        Float3BuildData delta = Subtract(destinationOrigin, sourceOrigin);
        Float3BuildData sourceBoundsMidPoint = ToPoint(instance!.Bounds.MidPoint);
        Float3BuildData destinationBoundsMidPoint =
            Add(sourceBoundsMidPoint, delta);
        Float3BuildData sourceLightingOrigin = ToPoint(instance.LightingOrigin);
        Float3BuildData destinationLightingOrigin =
            Add(sourceLightingOrigin, delta);

        if ((draw.Flags & GroundLightingFlag) != 0)
        {
            return Failed(
                StaticModelLightingPreservationIssueKind.GroundLightingIsBaked,
                $"Static model {staticModelOrdinal} uses serialized ground lighting (Flags & 0x02); translation would require a lighting rebuild.");
        }

        foreach ((Float3BuildData point, string name) in new[]
                 {
                     (sourceBoundsMidPoint, "source bounds midpoint"),
                     (sourceLightingOrigin, "source lighting origin")
                 })
        {
            if (!TrySelectReflectionProbe(
                    world,
                    point,
                    out byte selected,
                    out string? failure))
            {
                issues.Add(new(
                    StaticModelLightingPreservationIssueKind
                        .InvalidReflectionProbeState,
                    failure!));
            }
            else if (selected != draw.ReflectionProbeIndex)
            {
                issues.Add(new(
                    StaticModelLightingPreservationIssueKind
                        .ReflectionProbeSourceCalibrationFailed,
                    $"The {name} selects reflection probe {selected}, but the imported row stores {draw.ReflectionProbeIndex}."));
            }
        }
        foreach ((Float3BuildData point, string name) in new[]
                 {
                     (destinationBoundsMidPoint, "destination bounds midpoint"),
                     (destinationLightingOrigin, "destination lighting origin")
                 })
        {
            if (!TrySelectReflectionProbe(
                    world,
                    point,
                    out byte selected,
                    out string? failure))
            {
                issues.Add(new(
                    StaticModelLightingPreservationIssueKind
                        .InvalidReflectionProbeState,
                    failure!));
            }
            else if (selected != draw.ReflectionProbeIndex)
            {
                issues.Add(new(
                    StaticModelLightingPreservationIssueKind
                        .ReflectionProbeDestinationWouldChange,
                    $"The {name} selects reflection probe {selected}, not imported probe {draw.ReflectionProbeIndex}."));
            }
        }
        if (issues.Count != 0)
            return new(issues, null);

        if (!TryValidatePrimaryLightState(world, comWorld, out string? lightFailure))
        {
            return Failed(
                StaticModelLightingPreservationIssueKind.InvalidPrimaryLightState,
                lightFailure!);
        }
        if (draw.PrimaryLightIndex != 0 &&
            draw.PrimaryLightIndex != world.SunPrimaryLightIndex)
        {
            return Failed(
                StaticModelLightingPreservationIssueKind.UnsupportedNonSunPrimaryLight,
                $"Static model {staticModelOrdinal} stores non-sun primary light {draw.PrimaryLightIndex}; the initial preservation slice does not rewrite non-sun ownership.");
        }

        if (!TryExcludeNonSunAssociations(
                world,
                comWorld,
                instance.Bounds,
                destinationBoundsMidPoint,
                out StaticModelLightingPreservationIssue? associationIssue))
        {
            return new([associationIssue!], null);
        }

        if (!TryReadLightGridContributors(
                world.LightGrid,
                sourceLightingOrigin,
                out int[] sourceEntries,
                out bool sourceNeedsTrace,
                out string? sourceGridFailure))
        {
            return Failed(
                StaticModelLightingPreservationIssueKind
                    .SourceLightGridSampleUnavailable,
                sourceGridFailure!);
        }
        if (sourceNeedsTrace)
        {
            return Failed(
                StaticModelLightingPreservationIssueKind
                    .SourceLightGridSampleNeedsTrace,
                "The source light-grid sample has a contributing corner whose NeedsTrace byte is nonzero.");
        }
        if (!TryReadLightGridContributors(
                world.LightGrid,
                destinationLightingOrigin,
                out int[] destinationEntries,
                out bool destinationNeedsTrace,
                out string? destinationGridFailure))
        {
            return Failed(
                StaticModelLightingPreservationIssueKind
                    .DestinationLightGridSampleUnavailable,
                destinationGridFailure!);
        }
        if (destinationNeedsTrace)
        {
            return Failed(
                StaticModelLightingPreservationIssueKind
                    .DestinationLightGridSampleNeedsTrace,
                "The destination light-grid sample has a contributing corner whose NeedsTrace byte is nonzero.");
        }

        return new(
            [],
            new StaticModelLightingPreservationEvidence(
                staticModelOrdinal,
                sourceOrigin,
                destinationOrigin,
                sourceBoundsMidPoint,
                destinationBoundsMidPoint,
                sourceLightingOrigin,
                destinationLightingOrigin,
                draw.LightingHandle,
                draw.ReflectionProbeIndex,
                draw.PrimaryLightIndex,
                draw.Flags,
                draw.GroundLighting,
                Array.AsReadOnly(sourceEntries),
                Array.AsReadOnly(destinationEntries)));
    }

    private static bool TryGetStaticModel(
        GfxWorldAsset world,
        int ordinal,
        out GfxStaticModelDrawInst? draw,
        out GfxStaticModelInst? instance,
        out string? failure)
    {
        draw = null;
        instance = null;
        failure = null;
        if (world.Dpvs.SModelCount != world.Dpvs.SModelDrawInsts.Count ||
            world.Dpvs.SModelCount != world.Dpvs.SModelInsts.Count ||
            (uint)ordinal >= world.Dpvs.SModelCount)
        {
            failure = "The GfxWorld static-model count, parallel arrays, and requested ordinal do not agree.";
            return false;
        }
        draw = world.Dpvs.SModelDrawInsts[ordinal];
        instance = world.Dpvs.SModelInsts[ordinal];
        if (draw.Placement.Origin.Count != 3 ||
            !IsFinite(ToPoint(draw.Placement.Origin)) ||
            !IsValid(instance.Bounds) ||
            !IsFinite(ToPoint(instance.LightingOrigin)))
        {
            failure = $"Static model {ordinal} has invalid placement, bounds, or lighting-origin geometry.";
            return false;
        }
        return true;
    }

    private static bool TrySelectReflectionProbe(
        GfxWorldAsset world,
        Float3BuildData point,
        out byte selected,
        out string? failure)
    {
        selected = 0;
        failure = null;
        if (world.WorldDraw.ReflectionProbeCount >
            byte.MaxValue + 1u)
        {
            failure = "Reflection-probe cardinality exceeds its serialized byte index space.";
            return false;
        }
        int probeCount = (int)world.WorldDraw.ReflectionProbeCount;
        int cellCount = world.DpvsPlanes.CellCount;
        if (probeCount <= 0 ||
            world.WorldDraw.ReflectionProbeOrigins.Count != probeCount ||
            cellCount < 0 || cellCount >= ushort.MaxValue ||
            world.Cells.Count != cellCount ||
            world.PlaneCount != world.DpvsPlanes.Planes.Count ||
            world.NodeCount != world.DpvsPlanes.Nodes.Count ||
            world.DpvsPlanes.Nodes.Count == 0)
        {
            failure = "Reflection-probe or DPVS cell cardinality is invalid.";
            return false;
        }

        IReadOnlyList<ushort> nodes = world.DpvsPlanes.Nodes;
        var visited = new HashSet<int>();
        int nodePosition = 0;
        int internalBase = cellCount + 1;
        while (true)
        {
            if ((uint)nodePosition >= (uint)nodes.Count ||
                !visited.Add(nodePosition))
            {
                failure = "Reflection-probe DPVS traversal escaped storage or formed a cycle.";
                return false;
            }
            int token = nodes[nodePosition];
            if (token < internalBase)
            {
                int cellIndex = token - 1;
                IReadOnlyList<byte> candidates;
                if (cellIndex == -1)
                {
                    candidates = Enumerable.Range(1, probeCount - 1)
                        .Select(index => checked((byte)index))
                        .ToArray();
                }
                else
                {
                    if ((uint)cellIndex >= (uint)world.Cells.Count ||
                        world.Cells[cellIndex].ReflectionProbeCount !=
                            world.Cells[cellIndex].ReflectionProbes.Count)
                    {
                        failure = "A reflection-probe DPVS leaf references an invalid cell.";
                        return false;
                    }
                    candidates = world.Cells[cellIndex].ReflectionProbes;
                }

                float bestDistance = float.MaxValue;
                foreach (byte candidate in candidates)
                {
                    if (candidate >= probeCount)
                    {
                        failure = "A cell reflection-probe list contains an out-of-range index.";
                        return false;
                    }
                    GfxReflectionProbe probe =
                        world.WorldDraw.ReflectionProbeOrigins[candidate];
                    float x = point.X - probe.OffsetX;
                    float y = point.Y - probe.OffsetY;
                    float z = point.Z - probe.OffsetZ;
                    float squaredDistance = x * x + y * y + z * z;
                    if (!float.IsFinite(squaredDistance))
                    {
                        failure = "Reflection-probe distance evaluation is non-finite.";
                        return false;
                    }
                    if (squaredDistance < bestDistance)
                    {
                        bestDistance = squaredDistance;
                        selected = candidate;
                    }
                }
                return true;
            }

            int planeIndex = token - internalBase;
            if ((uint)planeIndex >= (uint)world.DpvsPlanes.Planes.Count ||
                nodePosition + 1 >= nodes.Count)
            {
                failure = "A reflection-probe DPVS node references invalid plane or child storage.";
                return false;
            }
            DpvsPlane plane = world.DpvsPlanes.Planes[planeIndex];
            float distance =
                point.X * plane.NormalX +
                point.Y * plane.NormalY +
                point.Z * plane.NormalZ -
                plane.Distance;
            if (!float.IsFinite(distance))
            {
                failure = "A reflection-probe DPVS plane evaluation is non-finite.";
                return false;
            }
            nodePosition += distance <= 0f
                ? nodes[nodePosition + 1]
                : 2;
        }
    }

    private static bool TryValidatePrimaryLightState(
        GfxWorldAsset world,
        ComWorldBuildData comWorld,
        out string? failure)
    {
        failure = null;
        if (world.PrimaryLightCount <= 0 ||
            world.PrimaryLightCount > byte.MaxValue + 1 ||
            world.PrimaryLightCount != comWorld.PrimaryLights.Count ||
            world.PrimaryLightCount != world.LightRegions.Count ||
            world.SunPrimaryLightIndex < 0 ||
            world.SunPrimaryLightIndex >= world.PrimaryLightCount ||
            world.LightGrid.SunPrimaryLightIndex !=
                (uint)world.SunPrimaryLightIndex)
        {
            failure = "GfxWorld, ComWorld, light-region, and sun primary-light cardinality does not form one index space.";
            return false;
        }
        return true;
    }

    private static bool TryExcludeNonSunAssociations(
        GfxWorldAsset world,
        ComWorldBuildData comWorld,
        AssetBounds sourceBounds,
        Float3BuildData destinationMidPoint,
        out StaticModelLightingPreservationIssue? issue)
    {
        issue = null;
        var destinationBounds = new AssetBounds
        {
            MidPoint = new AssetVec3
            {
                X = destinationMidPoint.X,
                Y = destinationMidPoint.Y,
                Z = destinationMidPoint.Z
            },
            HalfSize = sourceBounds.HalfSize
        };
        for (int index = world.SunPrimaryLightIndex + 1;
             index < world.PrimaryLightCount;
             index++)
        {
            ComPrimaryLightBuildData light = comWorld.PrimaryLights[index];
            bool sourcePotential = IntersectsLightSphere(sourceBounds, light);
            bool destinationPotential =
                IntersectsLightSphere(destinationBounds, light);
            if (!sourcePotential && !destinationPotential)
                continue;

            if (light.Type == 2)
            {
                issue = new(
                    StaticModelLightingPreservationIssueKind
                        .UnsupportedSpotLightAssociation,
                    $"Primary light {index} is a potentially affecting type-2 spot light; spot-cone preservation is outside the initial translation slice.");
                return false;
            }
            if (light.Type != 3)
            {
                issue = new(
                    StaticModelLightingPreservationIssueKind
                        .InvalidPrimaryLightState,
                    $"Potentially affecting primary light {index} has unsupported type {light.Type}.");
                return false;
            }
            if (ContainsLightSphere(sourceBounds, light) ||
                ContainsLightSphere(destinationBounds, light))
            {
                issue = new(
                    StaticModelLightingPreservationIssueKind
                        .NonSunPrimaryLightAssociation,
                    $"Primary light {index} contains the source or destination bounds; preserving a 0/sun assignment is not proven.");
                return false;
            }

            issue = new(
                StaticModelLightingPreservationIssueKind
                    .NonSunPrimaryLightAssociation,
                $"Primary light {index} intersects the source or destination bounds without full containment; non-association cannot be proven.");
            return false;
        }
        return true;
    }

    private static bool IntersectsLightSphere(
        AssetBounds bounds,
        ComPrimaryLightBuildData light)
    {
        if (!IsValidLight(light))
            return true;
        float x = MathF.Max(
            MathF.Abs(bounds.MidPoint.X - light.Origin.X) -
                bounds.HalfSize.X,
            0f);
        float y = MathF.Max(
            MathF.Abs(bounds.MidPoint.Y - light.Origin.Y) -
                bounds.HalfSize.Y,
            0f);
        float z = MathF.Max(
            MathF.Abs(bounds.MidPoint.Z - light.Origin.Z) -
                bounds.HalfSize.Z,
            0f);
        return x * x + y * y + z * z < light.Radius * light.Radius;
    }

    private static bool ContainsLightSphere(
        AssetBounds bounds,
        ComPrimaryLightBuildData light)
    {
        float x = MathF.Abs(bounds.MidPoint.X - light.Origin.X) +
            bounds.HalfSize.X;
        float y = MathF.Abs(bounds.MidPoint.Y - light.Origin.Y) +
            bounds.HalfSize.Y;
        float z = MathF.Abs(bounds.MidPoint.Z - light.Origin.Z) +
            bounds.HalfSize.Z;
        return x * x + y * y + z * z < light.Radius * light.Radius;
    }

    private static bool IsValidLight(ComPrimaryLightBuildData light) =>
        IsFinite(light.Origin) &&
        float.IsFinite(light.Radius) &&
        light.Radius > 0f;

    private static bool TryReadLightGridContributors(
        GfxLightGrid grid,
        Float3BuildData point,
        out int[] contributors,
        out bool needsTrace,
        out string? failure)
    {
        contributors = [];
        needsTrace = false;
        failure = null;
        if (!IsFinite(point) ||
            grid.Mins.Count != 3 ||
            grid.Maxs.Count != 3 ||
            grid.RowAxis > 1 ||
            grid.ColAxis > 1 ||
            grid.RowAxis == grid.ColAxis ||
            grid.EntryCount != grid.Entries.Count ||
            grid.ColorCount != grid.Colors.Count ||
            grid.Entries.Count == 0 ||
            grid.Colors.Count == 0 ||
            grid.RawRowDataSize != grid.RawRowData.Count)
        {
            failure = "The light-grid header or materialized arrays are invalid.";
            return false;
        }

        int rowAxis = checked((int)grid.RowAxis);
        int colAxis = checked((int)grid.ColAxis);
        int expectedRows = grid.Maxs[rowAxis] - grid.Mins[rowAxis] + 1;
        if (expectedRows <= 0 || grid.RowDataStart.Count != expectedRows)
        {
            failure = "The light-grid row table does not match its declared row range.";
            return false;
        }

        Span<int> position = stackalloc int[3];
        position[0] = ((int)MathF.Floor(point.X) + 0x20000) >> 5;
        position[1] = ((int)MathF.Floor(point.Y) + 0x20000) >> 5;
        position[2] = ((int)MathF.Floor(point.Z) + 0x20000) >> 6;
        for (int axis = 0; axis < 3; axis++)
        {
            if (position[axis] < grid.Mins[axis] ||
                position[axis] + 1 > grid.Maxs[axis])
            {
                failure = "The light-grid sample lies outside the complete trilinear lattice.";
                return false;
            }
        }

        float rowLerp =
            (Axis(point, rowAxis) - GridOrigin) / 32f -
            position[rowAxis];
        float colLerp =
            (Axis(point, colAxis) - GridOrigin) / 32f -
            position[colAxis];
        float zLerp =
            (point.Z - GridOrigin) / 64f -
            position[2];
        Span<float> weights = stackalloc float[8];
        SetWeights(weights, rowLerp, colLerp, zLerp);
        Span<int> indices = stackalloc int[8];
        indices.Fill(-1);
        if (!ReadQuad(grid, position, indices, 0))
        {
            failure = "The light-grid source row is malformed.";
            return false;
        }
        position[rowAxis]++;
        if (!ReadQuad(grid, position, indices, 4))
        {
            failure = "The adjacent light-grid row is malformed.";
            return false;
        }

        var result = new List<int>(8);
        for (int corner = 0; corner < 8; corner++)
        {
            if (weights[corner] < MinimumCornerWeight)
                continue;
            int entryIndex = indices[corner];
            if ((uint)entryIndex >= (uint)grid.Entries.Count)
            {
                failure = $"Contributing light-grid corner {corner} has no valid entry.";
                return false;
            }
            GfxLightGridEntry entry = grid.Entries[entryIndex];
            if (entry.ColorsIndex >= grid.Colors.Count ||
                grid.Colors[entry.ColorsIndex].RgbBytes.Count !=
                    GfxLightGridColors.SerializedSize)
            {
                failure = $"Light-grid entry {entryIndex} references an invalid color row.";
                return false;
            }
            result.Add(entryIndex);
            needsTrace |= entry.NeedsTrace != 0;
        }
        if (result.Count == 0)
        {
            failure = "The light-grid sample has no contributing corners.";
            return false;
        }
        contributors = result.Distinct().ToArray();
        return true;
    }

    private static bool ReadQuad(
        GfxLightGrid grid,
        ReadOnlySpan<int> position,
        Span<int> entries,
        int outputOffset)
    {
        int rowAxis = checked((int)grid.RowAxis);
        int colAxis = checked((int)grid.ColAxis);
        int rowIndex = position[rowAxis] - grid.Mins[rowAxis];
        if ((uint)rowIndex >= (uint)grid.RowDataStart.Count)
            return false;
        ushort wordOffset = grid.RowDataStart[rowIndex];
        if (wordOffset == ushort.MaxValue)
            return false;
        int offset = wordOffset * 4;
        IReadOnlyList<byte> raw = grid.RawRowData;
        if (offset + 12 > raw.Count)
            return false;
        int colStart = ReadUInt16(raw, offset);
        int colCount = ReadUInt16(raw, offset + 2);
        int zStart = ReadUInt16(raw, offset + 4);
        int zCount = ReadUInt16(raw, offset + 6);
        uint firstRaw = ReadUInt32(raw, offset + 8);
        if (firstRaw > int.MaxValue)
            return false;
        int firstEntry = (int)firstRaw;
        int column = position[colAxis] - colStart;
        int z = position[2] - zStart;
        if (column < 0 || column + 1 >= colCount ||
            z < 0 || z + 1 >= zCount)
            return false;

        int cursor = offset + 12;
        int runSize = zCount > byte.MaxValue ? 4 : 3;
        while (true)
        {
            if (!TryReadRun(raw, cursor, zCount,
                    out int columns, out int depth, out int baseZ) ||
                columns <= 0)
            {
                return false;
            }
            if (column < columns)
            {
                if (depth <= 0)
                    return false;
                int localZ = z - baseZ;
                if (localZ < 0 || localZ + 1 >= depth)
                    return false;
                int first = firstEntry + column * depth + localZ;
                entries[outputOffset] = first;
                entries[outputOffset + 1] = first + 1;
                if (column + 1 < columns)
                {
                    entries[outputOffset + 2] = first + depth;
                    entries[outputOffset + 3] = first + depth + 1;
                    return true;
                }

                int nextCursor = cursor + runSize;
                int nextFirst = firstEntry + depth * columns;
                if (!TryReadRun(raw, nextCursor, zCount,
                        out _, out int nextDepth, out int nextBaseZ) ||
                    nextDepth <= 0)
                {
                    return false;
                }
                int nextLocalZ = z - nextBaseZ;
                if (nextLocalZ < 0 || nextLocalZ + 1 >= nextDepth)
                    return false;
                entries[outputOffset + 2] = nextFirst + nextLocalZ;
                entries[outputOffset + 3] =
                    nextFirst + nextLocalZ + 1;
                return true;
            }
            column -= columns;
            firstEntry += depth * columns;
            cursor += depth != 0 ? runSize : 2;
        }
    }

    private static bool TryReadRun(
        IReadOnlyList<byte> raw,
        int offset,
        int zCount,
        out int columns,
        out int depth,
        out int baseZ)
    {
        columns = depth = baseZ = 0;
        int size = zCount > byte.MaxValue ? 4 : 3;
        if (offset < 0 || offset + size > raw.Count)
            return false;
        columns = raw[offset];
        depth = raw[offset + 1];
        baseZ = raw[offset + 2];
        if (size == 4)
            baseZ |= raw[offset + 3] << 8;
        return true;
    }

    private static ushort ReadUInt16(IReadOnlyList<byte> raw, int offset) =>
        checked((ushort)((raw[offset] << 8) | raw[offset + 1]));

    private static uint ReadUInt32(IReadOnlyList<byte> raw, int offset) =>
        (uint)raw[offset] << 24 |
        (uint)raw[offset + 1] << 16 |
        (uint)raw[offset + 2] << 8 |
        raw[offset + 3];

    private static void SetWeights(
        Span<float> weights,
        float row,
        float col,
        float z)
    {
        float inverseRow = 1f - row;
        float inverseCol = 1f - col;
        float inverseZ = 1f - z;
        weights[0] = inverseRow * inverseCol * inverseZ;
        weights[1] = inverseRow * inverseCol * z;
        weights[2] = inverseRow * col * inverseZ;
        weights[3] = inverseRow * col * z;
        weights[4] = row * inverseCol * inverseZ;
        weights[5] = row * inverseCol * z;
        weights[6] = row * col * inverseZ;
        weights[7] = row * col * z;
    }

    private static float Axis(Float3BuildData value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            2 => value.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };

    private static bool IsValid(AssetBounds bounds) =>
        IsFinite(ToPoint(bounds.MidPoint)) &&
        IsFinite(ToPoint(bounds.HalfSize)) &&
        bounds.HalfSize.X >= 0f &&
        bounds.HalfSize.Y >= 0f &&
        bounds.HalfSize.Z >= 0f;

    private static bool IsFinite(Float3BuildData value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static Float3BuildData ToPoint(IReadOnlyList<float> value) =>
        new(value[0], value[1], value[2]);

    private static Float3BuildData ToPoint(AssetVec3 value) =>
        new(value.X, value.Y, value.Z);

    private static Float3BuildData Add(
        Float3BuildData left,
        Float3BuildData right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static Float3BuildData Subtract(
        Float3BuildData left,
        Float3BuildData right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static StaticModelLightingPreservationEligibility Failed(
        StaticModelLightingPreservationIssueKind kind,
        string detail) =>
        new([new(kind, detail)], null);
}
