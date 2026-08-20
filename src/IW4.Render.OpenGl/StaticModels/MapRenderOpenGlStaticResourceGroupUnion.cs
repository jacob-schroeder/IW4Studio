namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// Allocation-free, deterministic union of resource-group selections gathered
/// from multiple prefetch cameras for one immutable resource plan.
/// </summary>
internal sealed class MapRenderOpenGlStaticResourceGroupUnion
{
    private readonly bool[] _included;
    private readonly int[] _groups;
    private int _groupCount;
    private bool _sorted = true;

    public MapRenderOpenGlStaticResourceGroupUnion(int groupCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCount);
        _included = new bool[groupCount];
        _groups = new int[groupCount];
    }

    public int Capacity => _groups.Length;

    public int Count => _groupCount;

    /// <summary>
    /// Returns the selected resource groups in stable ascending plan order.
    /// The span remains valid until the next <see cref="Add"/> or
    /// <see cref="Reset"/>.
    /// </summary>
    public ReadOnlySpan<int> Groups
    {
        get
        {
            if (!_sorted)
            {
                Array.Sort(_groups, 0, _groupCount);
                _sorted = true;
            }
            return _groups.AsSpan(0, _groupCount);
        }
    }

    public void Add(ReadOnlySpan<int> selectedGroups)
    {
        // Validate the whole input before mutating the reusable union so a
        // malformed selection cannot leave a partially admitted plan.
        foreach (int groupIndex in selectedGroups)
        {
            if ((uint)groupIndex >= (uint)_included.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(selectedGroups),
                    "A selected resource group is outside the immutable plan.");
            }
        }

        foreach (int groupIndex in selectedGroups)
        {
            if (_included[groupIndex])
                continue;

            _included[groupIndex] = true;
            _groups[_groupCount++] = groupIndex;
            _sorted = _groupCount <= 1;
        }
    }

    public void Reset()
    {
        for (int groupOrdinal = 0;
             groupOrdinal < _groupCount;
             groupOrdinal++)
        {
            _included[_groups[groupOrdinal]] = false;
        }
        _groupCount = 0;
        _sorted = true;
    }
}
