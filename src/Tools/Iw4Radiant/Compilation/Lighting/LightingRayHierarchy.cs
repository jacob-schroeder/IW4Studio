using System.Numerics;

namespace Iw4Radiant.Compilation.Lighting;

// Immutable, preorder bounds tree for the bake's world faces and model triangles.
internal sealed class LightingRayHierarchy
{
    private readonly Node[] _nodes;

    private readonly record struct Node(Vector3 Minimum, Vector3 Maximum, int Surface, int Escape);

    internal LightingRayHierarchy(IReadOnlyList<MapRenderSurface> surfaces, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (surfaces.Count == 0)
        {
            _nodes = [];
            return;
        }
        var bounds = new (Vector3 Minimum, Vector3 Maximum)[surfaces.Count];
        var indices = new int[surfaces.Count];
        for (int index = 0; index < surfaces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
            foreach (Vector3 vertex in surfaces[index].Vertices)
            {
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }
            for (int axis = 0; axis < 3; axis++)
            {
                // Broadphase slack only: include planar bounds and float edge hits.
                // The original triangle/barycentric test remains the final authority.
                double padding = 0.001 * (1 + (double)maximum[axis] - minimum[axis]);
                minimum[axis] = MathF.BitDecrement((float)(minimum[axis] - padding));
                maximum[axis] = MathF.BitIncrement((float)(maximum[axis] + padding));
            }
            bounds[index] = (minimum, maximum);
            indices[index] = index;
        }
        var comparers = new IComparer<int>[3];
        for (int axis = 0; axis < 3; axis++)
        {
            int coordinate = axis;
            comparers[axis] = Comparer<int>.Create((left, right) =>
            {
                double a = (double)bounds[left].Minimum[coordinate] + bounds[left].Maximum[coordinate];
                double b = (double)bounds[right].Minimum[coordinate] + bounds[right].Maximum[coordinate];
                int position = a.CompareTo(b);
                return position != 0 ? position : left.CompareTo(right);
            });
        }
        _nodes = new Node[checked(surfaces.Count * 2 - 1)];
        int cursor = 0;
        Build(0, indices.Length);

        void Build(int start, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int node = cursor++;
            Vector3 minimum = bounds[indices[start]].Minimum, maximum = bounds[indices[start]].Maximum;
            for (int item = start + 1; item < start + count; item++)
            {
                minimum = Vector3.Min(minimum, bounds[indices[item]].Minimum);
                maximum = Vector3.Max(maximum, bounds[indices[item]].Maximum);
            }
            if (count == 1)
            {
                _nodes[node] = new Node(minimum, maximum, indices[start], cursor);
                return;
            }
            int axis = 0;
            for (int coordinate = 1; coordinate < 3; coordinate++)
                if ((double)maximum[coordinate] - minimum[coordinate] > (double)maximum[axis] - minimum[axis])
                    axis = coordinate;
            Array.Sort(indices, start, count, comparers[axis]);
            int leftCount = count / 2;
            Build(start, leftCount);
            Build(start + leftCount, count - leftCount);
            _nodes[node] = new Node(minimum, maximum, -1, cursor);
        }
    }

    internal RayEnumerator Query(Vector3 origin, Vector3 direction, double maximumDistance,
        CancellationToken cancellationToken) => new(this, origin, direction, maximumDistance, cancellationToken);

    // Escape indices skip whole disjoint subtrees without a traversal stack or heap allocation.
    internal struct RayEnumerator
    {
        private readonly LightingRayHierarchy _hierarchy;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly double _maximumDistance;
        private readonly CancellationToken _cancellationToken;
        private int _cursor;
        private int _visited;

        internal RayEnumerator(LightingRayHierarchy hierarchy, Vector3 origin, Vector3 direction,
            double maximumDistance, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hierarchy = hierarchy;
            _origin = origin;
            _direction = direction;
            _maximumDistance = maximumDistance;
            _cancellationToken = cancellationToken;
        }

        internal int Current { get; private set; }

        internal bool MoveNext()
        {
            while (_cursor < _hierarchy._nodes.Length)
            {
                if ((_visited++ & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
                Node node = _hierarchy._nodes[_cursor];
                if (!IntersectsBounds(node, _origin, _direction, _maximumDistance))
                {
                    _cursor = node.Escape;
                    continue;
                }
                _cursor++;
                if (node.Surface < 0) continue;
                Current = node.Surface;
                return true;
            }
            return false;
        }
    }

    private static bool IntersectsBounds(Node bounds, Vector3 origin, Vector3 direction, double maximumDistance)
    {
        // Account for float triangle arithmetic at the origin and finite light endpoint.
        // Expanding this broadphase never changes the exact distance/contact rejection.
        const double roundoff = 8.0 / 8388608.0;
        double enter = 0, exit = maximumDistance + Math.Abs(maximumDistance) * roundoff + 0.001;
        for (int axis = 0; axis < 3; axis++)
        {
            double padding = (Math.Abs((double)origin[axis]) +
                Math.Max(Math.Abs((double)bounds.Minimum[axis]), Math.Abs((double)bounds.Maximum[axis]))) * roundoff;
            double minimum = bounds.Minimum[axis] - padding, maximum = bounds.Maximum[axis] + padding;
            if (direction[axis] == 0)
            {
                if (origin[axis] < minimum || origin[axis] > maximum) return false;
                continue;
            }
            double first = (minimum - origin[axis]) / direction[axis];
            double second = (maximum - origin[axis]) / direction[axis];
            enter = Math.Max(enter, Math.BitDecrement(Math.Min(first, second)));
            exit = Math.Min(exit, Math.BitIncrement(Math.Max(first, second)));
            if (enter > exit) return false;
        }
        return true;
    }
}
