using IW4.Render.EditorPreview;

namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// Retains the immutable draw groups created for each progressively admitted
/// static resource group. Admission is monotonic for a loaded scene, so a
/// group's bounds, authored passes, and classification only need to be built
/// once. Publication merely flattens the currently executable entries and
/// reapplies the eager builder's contiguous source ordinals.
/// </summary>
internal sealed class MapRenderOpenGlProgressiveStaticDrawGroupCache
{
    private readonly MapRenderEditorDrawGroup<
        GlTexturedDrawCommand>[]?[] _groupsByResourceGroup;
    private readonly MapRenderEditorDrawGroup<
        GlTexturedDrawCommand>[]?[] _publishedGroupsByResourceGroup;
    private readonly long[] _publishedFirstSourceOrdinals;
    private readonly bool[] _publishedExecutableGroups;
    private MapRenderEditorDrawGroup<
        GlTexturedDrawCommand>[]? _publishedGroups;
    private int _publishedPrefixGroupCount;
    private long _publishedFirstStaticSourceOrdinal;
    private bool _publicationDirty = true;

    public MapRenderOpenGlProgressiveStaticDrawGroupCache(
        int resourceGroupCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resourceGroupCount);
        _groupsByResourceGroup =
            new MapRenderEditorDrawGroup<
                GlTexturedDrawCommand>[resourceGroupCount][];
        _publishedGroupsByResourceGroup =
            new MapRenderEditorDrawGroup<
                GlTexturedDrawCommand>[resourceGroupCount][];
        _publishedFirstSourceOrdinals = new long[resourceGroupCount];
        _publishedExecutableGroups = new bool[resourceGroupCount];
    }

    public int ResourceGroupCount => _groupsByResourceGroup.Length;

    public bool IsCreated(int resourceGroupIndex)
    {
        ValidateGroupIndex(resourceGroupIndex);
        return _groupsByResourceGroup[resourceGroupIndex] is not null;
    }

    public void Store(
        int resourceGroupIndex,
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] groups)
    {
        ValidateGroupIndex(resourceGroupIndex);
        ArgumentNullException.ThrowIfNull(groups);
        if (_groupsByResourceGroup[resourceGroupIndex] is not null)
        {
            throw new InvalidOperationException(
                "A progressive static draw group may only be cached once.");
        }

        _groupsByResourceGroup[resourceGroupIndex] = groups;
        _publicationDirty = true;
    }

    public MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] Publish(
        IReadOnlyList<MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>> prefixGroups,
        ReadOnlySpan<bool> executableGroups,
        long firstStaticSourceOrdinal)
    {
        ArgumentNullException.ThrowIfNull(prefixGroups);
        if (executableGroups.Length != _groupsByResourceGroup.Length)
        {
            throw new ArgumentException(
                "Executable static groups must match the cache's immutable resource-group space.",
                nameof(executableGroups));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(
            firstStaticSourceOrdinal);

        if (!_publicationDirty &&
            _publishedGroups is { } previousPublication &&
            _publishedPrefixGroupCount == prefixGroups.Count &&
            _publishedFirstStaticSourceOrdinal ==
                firstStaticSourceOrdinal &&
            PrefixMatches(
                previousPublication,
                prefixGroups) &&
            executableGroups.SequenceEqual(
                _publishedExecutableGroups))
        {
            return previousPublication;
        }

        int groupCount = prefixGroups.Count;
        for (int resourceGroupIndex = 0;
             resourceGroupIndex < executableGroups.Length;
             resourceGroupIndex++)
        {
            if (!executableGroups[resourceGroupIndex])
                continue;
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] cached =
                _groupsByResourceGroup[resourceGroupIndex] ??
                throw new InvalidOperationException(
                    "An executable static resource group has no cached draw groups.");
            groupCount = checked(groupCount + cached.Length);
        }

        var result = new MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>[groupCount];
        int destination = 0;
        for (int prefixIndex = 0;
             prefixIndex < prefixGroups.Count;
             prefixIndex++)
        {
            result[destination++] = prefixGroups[prefixIndex];
        }

        long sourceOrdinal = firstStaticSourceOrdinal;
        for (int resourceGroupIndex = 0;
             resourceGroupIndex < executableGroups.Length;
             resourceGroupIndex++)
        {
            if (!executableGroups[resourceGroupIndex])
                continue;
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] cached =
                _groupsByResourceGroup[resourceGroupIndex]!;
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] published;
            if (_publishedGroupsByResourceGroup[resourceGroupIndex]
                    is { } previous &&
                _publishedFirstSourceOrdinals[resourceGroupIndex] ==
                    sourceOrdinal)
            {
                published = previous;
            }
            else
            {
                published =
                    new MapRenderEditorDrawGroup<
                        GlTexturedDrawCommand>[cached.Length];
                for (int groupIndex = 0;
                     groupIndex < cached.Length;
                     groupIndex++)
                {
                    published[groupIndex] =
                        cached[groupIndex].WithSourceOrdinal(
                            checked(sourceOrdinal + groupIndex));
                }
                _publishedGroupsByResourceGroup[resourceGroupIndex] =
                    published;
                _publishedFirstSourceOrdinals[resourceGroupIndex] =
                    sourceOrdinal;
            }

            published.CopyTo(result, destination);
            destination += published.Length;
            sourceOrdinal = checked(sourceOrdinal + published.Length);
        }

        executableGroups.CopyTo(_publishedExecutableGroups);
        _publishedGroups = result;
        _publishedPrefixGroupCount = prefixGroups.Count;
        _publishedFirstStaticSourceOrdinal =
            firstStaticSourceOrdinal;
        _publicationDirty = false;
        return result;
    }

    private static bool PrefixMatches(
        IReadOnlyList<MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>> publishedGroups,
        IReadOnlyList<MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>> prefixGroups)
    {
        if (publishedGroups.Count < prefixGroups.Count)
            return false;
        for (int prefixIndex = 0;
             prefixIndex < prefixGroups.Count;
             prefixIndex++)
        {
            if (!ReferenceEquals(
                    publishedGroups[prefixIndex],
                    prefixGroups[prefixIndex]))
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateGroupIndex(int resourceGroupIndex)
    {
        if ((uint)resourceGroupIndex >=
            (uint)_groupsByResourceGroup.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourceGroupIndex));
        }
    }
}
