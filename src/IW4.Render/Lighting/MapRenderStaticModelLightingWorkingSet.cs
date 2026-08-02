using System.Numerics;

namespace IW4.Render.Lighting;

internal readonly record struct MapRenderStaticModelLightingAssignment(
    int ObjectIndex,
    int EntryIndex,
    int ReplacedObjectIndex);

/// <summary>
/// Mirrors the native static-model model-lighting handle cache. Object
/// identities are independent from physical atlas entries, resident entries
/// are retained while warm, and an entry may be recycled only after four
/// complete frames without use.
/// </summary>
internal sealed class MapRenderStaticModelLightingWorkingSet
{
    internal const uint RecycleAgeFrames = 4;

    private readonly int[] _entryByObject;
    private readonly int[] _objectByEntry;
    private readonly uint[] _lastUsedFrameByEntry;
    private readonly int[] _freeableEntries;
    private readonly MapRenderStaticModelLightingAssignment[]
        _dirtyAssignments;
    private int _assignedEntryCount;
    private int _freeableEntryCount;
    private int _dirtyAssignmentCount;
    private uint _frameCount;

    public MapRenderStaticModelLightingWorkingSet(
        int objectCount,
        int entryCapacity =
            MapRenderStaticModelLightingAtlas.StaticEntryCapacity)
    {
        if (objectCount < 0)
            throw new ArgumentOutOfRangeException(nameof(objectCount));
        if (entryCapacity is <= 0 or >
            MapRenderStaticModelLightingAtlas.StaticEntryCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(entryCapacity));
        }

        _entryByObject = new int[objectCount];
        Array.Fill(_entryByObject, -1);
        _objectByEntry = new int[entryCapacity];
        Array.Fill(_objectByEntry, -1);
        _lastUsedFrameByEntry = new uint[entryCapacity];
        _freeableEntries = new int[entryCapacity];
        _dirtyAssignments =
            new MapRenderStaticModelLightingAssignment[entryCapacity];
        CoordinatesByObject = new Vector4[objectCount];
    }

    public int ObjectCount => _entryByObject.Length;

    public int EntryCapacity => _objectByEntry.Length;

    public int ResidentEntryCount => _assignedEntryCount;

    public uint FrameCount => _frameCount;

    public ulong AssignmentGeneration { get; private set; }

    public int AllocationMissCount { get; private set; }

    public int NewAssignmentCount { get; private set; }

    public int RecycledAssignmentCount { get; private set; }

    public Vector4[] CoordinatesByObject { get; }

    public ReadOnlySpan<MapRenderStaticModelLightingAssignment>
        DirtyAssignments =>
        _dirtyAssignments.AsSpan(0, _dirtyAssignmentCount);

    /// <summary>
    /// Advances one renderer frame and admits every visible object in the
    /// supplied object-index space.
    /// </summary>
    public void UpdateFrame(Span<bool> visibleByObject)
    {
        if (visibleByObject.Length != ObjectCount)
        {
            throw new ArgumentException(
                "Full working-set visibility must match the static-model object count.",
                nameof(visibleByObject));
        }

        AdvanceFrame();
        TouchVisibleResidents(visibleByObject);
        CollectFreeableEntries();
        AllocateVisibleMisses(visibleByObject);
        CompleteFrame();
    }

    /// <summary>
    /// Advances one renderer frame while considering only known renderable
    /// object identities. This avoids treating gaps in sparse viewer scene
    /// reconstruction as model-lighting consumers.
    /// </summary>
    public void UpdateFrame(
        Span<bool> visibleByObject,
        ReadOnlySpan<int> objectIndices)
    {
        AdvanceFrame();

        for (int index = 0; index < objectIndices.Length; index++)
        {
            int objectIndex = ValidateObjectIndex(
                objectIndices[index],
                visibleByObject);
            if (!visibleByObject[objectIndex])
                continue;
            int entryIndex = _entryByObject[objectIndex];
            if (entryIndex >= 0)
                _lastUsedFrameByEntry[entryIndex] = _frameCount;
        }

        CollectFreeableEntries();

        for (int index = 0; index < objectIndices.Length; index++)
        {
            int objectIndex = ValidateObjectIndex(
                objectIndices[index],
                visibleByObject);
            if (visibleByObject[objectIndex] &&
                _entryByObject[objectIndex] < 0)
            {
                AllocateOrReject(objectIndex, visibleByObject);
            }
        }
        CompleteFrame();
    }

    public bool TryGetEntryIndex(
        int objectIndex,
        out int entryIndex)
    {
        if ((uint)objectIndex >= (uint)ObjectCount)
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = _entryByObject[objectIndex];
        return entryIndex >= 0;
    }

    private void AdvanceFrame()
    {
        unchecked
        {
            _frameCount++;
        }
        _freeableEntryCount = 0;
        _dirtyAssignmentCount = 0;
        AllocationMissCount = 0;
        NewAssignmentCount = 0;
        RecycledAssignmentCount = 0;
    }

    private void CompleteFrame()
    {
        if (_dirtyAssignmentCount == 0)
            return;
        unchecked
        {
            AssignmentGeneration++;
        }
    }

    private void TouchVisibleResidents(
        ReadOnlySpan<bool> visibleByObject)
    {
        for (int objectIndex = 0;
             objectIndex < visibleByObject.Length;
             objectIndex++)
        {
            if (!visibleByObject[objectIndex])
                continue;
            int entryIndex = _entryByObject[objectIndex];
            if (entryIndex >= 0)
                _lastUsedFrameByEntry[entryIndex] = _frameCount;
        }
    }

    private void CollectFreeableEntries()
    {
        for (int entryIndex = 0;
             entryIndex < _assignedEntryCount;
             entryIndex++)
        {
            if (unchecked(
                    _frameCount -
                    _lastUsedFrameByEntry[entryIndex]) >=
                RecycleAgeFrames)
            {
                _freeableEntries[_freeableEntryCount++] =
                    entryIndex;
            }
        }
    }

    private void AllocateVisibleMisses(Span<bool> visibleByObject)
    {
        for (int objectIndex = 0;
             objectIndex < visibleByObject.Length;
             objectIndex++)
        {
            if (visibleByObject[objectIndex] &&
                _entryByObject[objectIndex] < 0)
            {
                AllocateOrReject(objectIndex, visibleByObject);
            }
        }
    }

    private void AllocateOrReject(
        int objectIndex,
        Span<bool> visibleByObject)
    {
        int entryIndex;
        if (_assignedEntryCount < EntryCapacity)
        {
            entryIndex = _assignedEntryCount++;
        }
        else
        {
            entryIndex = TakeFreeableEntry();
            if (entryIndex < 0)
            {
                visibleByObject[objectIndex] = false;
                AllocationMissCount++;
                return;
            }
        }

        int replacedObjectIndex = _objectByEntry[entryIndex];
        if (replacedObjectIndex >= 0)
        {
            _entryByObject[replacedObjectIndex] = -1;
            CoordinatesByObject[replacedObjectIndex] = default;
            RecycledAssignmentCount++;
        }
        else
        {
            NewAssignmentCount++;
        }

        _objectByEntry[entryIndex] = objectIndex;
        _entryByObject[objectIndex] = entryIndex;
        _lastUsedFrameByEntry[entryIndex] = _frameCount;
        CoordinatesByObject[objectIndex] =
            MapRenderStaticModelLightingAtlas.EntryCoordinates(
                entryIndex);
        _dirtyAssignments[_dirtyAssignmentCount++] = new(
            objectIndex,
            entryIndex,
            replacedObjectIndex);
    }

    private int TakeFreeableEntry()
    {
        while (_freeableEntryCount != 0)
        {
            int entryIndex =
                _freeableEntries[--_freeableEntryCount];
            // Native freeable handles are collected at frame start and can be
            // retouched later in that frame. The equality guard prevents a
            // current-frame owner from being recycled.
            if (_lastUsedFrameByEntry[entryIndex] != _frameCount)
                return entryIndex;
        }
        return -1;
    }

    private int ValidateObjectIndex(
        int objectIndex,
        ReadOnlySpan<bool> visibleByObject)
    {
        if ((uint)objectIndex >= (uint)ObjectCount ||
            (uint)objectIndex >= (uint)visibleByObject.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectIndex),
                objectIndex,
                "A working-set object identity is outside the supplied visibility and source storage.");
        }
        return objectIndex;
    }
}
