using System.Numerics;
using IW4.Render.Diagnostics;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private const int StaticModelLightingFullUploadThreshold = 256;

    private long UpdateStaticModelLightingWorkingSet()
    {
        if (_staticModelLightingWorkingSet is not { } workingSet)
        {
            RecordStaticModelLightingWorkingSetTelemetry(null);
            return _visibleScheduledStaticObjectCount;
        }

        workingSet.UpdateFrame(
            _visibleStaticObjects,
            _visibleStaticObjectWorklist.AsSpan(
                0,
                _visibleStaticObjectWorklistCount));
        CompactAdmittedStaticObjectWorklist();
        UploadStaticModelLightingAssignments(workingSet);
        RecordStaticModelLightingWorkingSetTelemetry(workingSet);
        return _visibleScheduledStaticObjectCount;
    }

    private void CompactAdmittedStaticObjectWorklist()
    {
        int outputCount = 0;
        for (int index = 0;
             index < _visibleScheduledStaticObjectCount;
             index++)
        {
            int objectIndex =
                _visibleStaticObjectWorklist[index];
            if ((uint)objectIndex >=
                (uint)_visibleStaticObjects.Length)
            {
                throw new InvalidOperationException(
                    "The compact scheduled-static worklist references an object outside frame visibility storage.");
            }
            if (_visibleStaticObjects[objectIndex])
            {
                _visibleStaticObjectWorklist[outputCount++] =
                    objectIndex;
            }
        }
        int admittedScheduledObjectCount = outputCount;

        for (int index = _visibleScheduledStaticObjectCount;
             index < _visibleStaticObjectWorklistCount;
             index++)
        {
            int objectIndex =
                _visibleStaticObjectWorklist[index];
            if ((uint)objectIndex >=
                (uint)_visibleStaticObjects.Length)
            {
                throw new InvalidOperationException(
                    "The compact fallback-static worklist references an object outside frame visibility storage.");
            }
            if (_visibleStaticObjects[objectIndex])
            {
                _visibleStaticObjectWorklist[outputCount++] =
                    objectIndex;
            }
        }

        _visibleScheduledStaticObjectCount =
            admittedScheduledObjectCount;
        _visibleStaticObjectWorklistCount = outputCount;
    }

    private void RecordStaticModelLightingWorkingSetTelemetry(
        MapRenderStaticModelLightingWorkingSet? workingSet)
    {
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter
                .StaticModelLightingResidentEntries,
            workingSet?.ResidentEntryCount ?? 0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter
                .StaticModelLightingAllocationMisses,
            workingSet?.AllocationMissCount ?? 0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter
                .StaticModelLightingNewAssignments,
            workingSet?.NewAssignmentCount ?? 0);
        _frameTelemetry.SetCounter(
            MapRenderFrameCounter
                .StaticModelLightingRecycledAssignments,
            workingSet?.RecycledAssignmentCount ?? 0);
    }

    private void UploadStaticModelLightingAssignments(
        MapRenderStaticModelLightingWorkingSet workingSet)
    {
        ReadOnlySpan<MapRenderStaticModelLightingAssignment>
            assignments = workingSet.DirtyAssignments;
        if (assignments.IsEmpty)
            return;
        MapRenderStaticModelLightingAtlas atlas =
            _staticModelLightingAtlas ??
            throw new InvalidOperationException(
                "A model-lighting working set has no source atlas.");
        if (_staticModelLightingAtlasTexture == 0)
        {
            throw new InvalidOperationException(
                "A model-lighting working set has no GPU cache texture.");
        }
        byte[] physicalAtlas =
            _staticModelLightingPhysicalRgbaBytes ??
            throw new InvalidOperationException(
                "A model-lighting working set has no renderer-local physical cache.");

        for (int index = 0; index < assignments.Length; index++)
        {
            MapRenderStaticModelLightingAssignment assignment =
                assignments[index];
            atlas.CopySourceTileToPhysicalAtlas(
                assignment.ObjectIndex,
                assignment.EntryIndex,
                physicalAtlas);
        }

        int previousTextureUnit = _state.GetActiveTextureUnit();
        try
        {
            _state.ActiveTexture(
                GenericStaticModelLightingTextureUnit);
            _state.BindTexture(
                TextureTarget.Texture3D,
                _staticModelLightingAtlasTexture);
            _gl.PixelStore(
                PixelStoreParameter.UnpackAlignment,
                1);
            if (assignments.Length >=
                StaticModelLightingFullUploadThreshold)
            {
                fixed (byte* pixels = physicalAtlas)
                {
                    _gl.TexSubImage3D(
                        TextureTarget.Texture3D,
                        0,
                        0,
                        0,
                        0,
                        MapRenderStaticModelLightingAtlas.Width,
                        MapRenderStaticModelLightingAtlas.Height,
                        MapRenderStaticModelLightingAtlas.Depth,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixels);
                }
            }
            else
            {
                for (int index = 0;
                     index < assignments.Length;
                     index++)
                {
                    MapRenderStaticModelLightingAssignment assignment =
                        assignments[index];
                    int x =
                        (assignment.EntryIndex &
                            (MapRenderStaticModelLightingAtlas
                                .EntriesPerRow - 1)) *
                        MapRenderStaticModelLightingAtlas.TileWidth;
                    int y =
                        (assignment.EntryIndex /
                            MapRenderStaticModelLightingAtlas
                                .EntriesPerRow) *
                        MapRenderStaticModelLightingAtlas.TileHeight;
                    ReadOnlySpan<byte> tile =
                        atlas.GetSourceTile(
                            assignment.ObjectIndex);
                    fixed (byte* pixels = tile)
                    {
                        _gl.TexSubImage3D(
                            TextureTarget.Texture3D,
                            0,
                            x,
                            y,
                            0,
                            MapRenderStaticModelLightingAtlas
                                .TileWidth,
                            MapRenderStaticModelLightingAtlas
                                .TileHeight,
                            MapRenderStaticModelLightingAtlas
                                .TileDepth,
                            PixelFormat.Rgba,
                            PixelType.UnsignedByte,
                            pixels);
                    }
                }
            }
        }
        finally
        {
            _state.ActiveTexture(previousTextureUnit);
        }
    }

    private Vector4[]? ResolveStaticModelLightingCoordinates(
        StaticInstanceBufferRuntime runtime)
    {
        if (runtime.LightingPayload !=
            MapRenderStaticInstanceLightingPayload
                .BaseLightingCoords)
        {
            return null;
        }
        return _staticModelLightingWorkingSet?.CoordinatesByObject ??
            throw new InvalidOperationException(
                "A row-0x39 static-instance buffer has no model-lighting working set.");
    }

    private StaticModelLightingEntryStage
        StageSelectedStaticModelLightingEntries(
        StaticInstanceBufferRuntime runtime,
        ReadOnlySpan<int> sourceIndices,
        bool force)
    {
        if (runtime.LightingPayload !=
            MapRenderStaticInstanceLightingPayload
                .BaseLightingCoords)
        {
            return default;
        }

        MapRenderStaticModelLightingWorkingSet workingSet =
            _staticModelLightingWorkingSet ??
            throw new InvalidOperationException(
                "A row-0x39 static-instance buffer has no model-lighting working set.");
        if (!force &&
            runtime.StaticModelLightingAssignmentGeneration ==
                workingSet.AssignmentGeneration)
        {
            return default;
        }
        bool changed = false;
        int[] current = runtime.CurrentLightingEntries;
        int[] next = runtime.NextLightingEntries;
        for (int destinationIndex = 0;
             destinationIndex < sourceIndices.Length;
             destinationIndex++)
        {
            int sourceIndex = sourceIndices[destinationIndex];
            if ((uint)sourceIndex >=
                (uint)runtime.Instances.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceIndices));
            }
            int objectIndex =
                runtime.Instances[sourceIndex].ObjectIndex;
            int entryIndex = workingSet.TryGetEntryIndex(
                objectIndex,
                out int assignedEntry)
                    ? assignedEntry
                    : -1;
            next[destinationIndex] = entryIndex;
            changed |= current[destinationIndex] != entryIndex;
        }
        return new(changed, Evaluated: true);
    }

    private StaticModelLightingEntryStage
        StageFullStaticModelLightingEntries(
            StaticInstanceBufferRuntime runtime,
            bool force)
    {
        if (runtime.LightingPayload !=
            MapRenderStaticInstanceLightingPayload
                .BaseLightingCoords)
        {
            return default;
        }

        MapRenderStaticModelLightingWorkingSet workingSet =
            _staticModelLightingWorkingSet ??
            throw new InvalidOperationException(
                "A row-0x39 static-instance buffer has no model-lighting working set.");
        if (!force &&
            runtime.StaticModelLightingAssignmentGeneration ==
                workingSet.AssignmentGeneration)
        {
            return default;
        }
        bool entryChanged = false;
        int[] current = runtime.CurrentLightingEntries;
        int[] next = runtime.NextLightingEntries;
        for (int sourceIndex = 0;
             sourceIndex < runtime.Instances.Length;
             sourceIndex++)
        {
            int objectIndex =
                runtime.Instances[sourceIndex].ObjectIndex;
            int entryIndex = workingSet.TryGetEntryIndex(
                objectIndex,
                out int assignedEntry)
                    ? assignedEntry
                    : -1;
            next[sourceIndex] = entryIndex;
            entryChanged |=
                current[sourceIndex] != entryIndex;
        }
        return new(entryChanged, Evaluated: true);
    }

    private static void CommitSelectedStaticModelLightingEntries(
        StaticInstanceBufferRuntime runtime,
        int count)
    {
        if (runtime.LightingPayload !=
            MapRenderStaticInstanceLightingPayload
                .BaseLightingCoords)
        {
            return;
        }
        Array.Copy(
            runtime.NextLightingEntries,
            runtime.CurrentLightingEntries,
            count);
    }

    private static void CommitFullStaticModelLightingEntries(
        StaticInstanceBufferRuntime runtime)
    {
        CommitSelectedStaticModelLightingEntries(
            runtime,
            runtime.Instances.Length);
    }

    private void CommitStaticModelLightingAssignmentGeneration(
        StaticInstanceBufferRuntime runtime)
    {
        if (runtime.LightingPayload !=
            MapRenderStaticInstanceLightingPayload
                .BaseLightingCoords)
        {
            return;
        }
        runtime.StaticModelLightingAssignmentGeneration =
            (_staticModelLightingWorkingSet ??
                throw new InvalidOperationException(
                    "A row-0x39 static-instance buffer has no model-lighting working set."))
            .AssignmentGeneration;
    }

    private readonly record struct StaticModelLightingEntryStage(
        bool Changed,
        bool Evaluated);
}
