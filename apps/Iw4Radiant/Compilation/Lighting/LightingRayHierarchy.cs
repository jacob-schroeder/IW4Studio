using System.Numerics;
using IW4.Render.WebGpu;

namespace Iw4Radiant.Compilation.Lighting;

// Immutable, preorder bounds tree for the bake's world faces and model triangles.
internal sealed class LightingRayHierarchy
{
    private const double BoundsRoundoff = 8.0 / 8388608.0;
    private readonly Node[] _nodes;
    private readonly int[] _surfaceNodes;

    private readonly struct Node
    {
        internal readonly Vector3 Minimum;
        internal readonly Vector3 Maximum;
        internal readonly double MaximumMagnitudeX;
        internal readonly double MaximumMagnitudeY;
        internal readonly double MaximumMagnitudeZ;
        internal readonly int Surface;
        internal readonly int Escape;

        internal Node(Vector3 minimum, Vector3 maximum, int surface, int escape)
        {
            Minimum = minimum;
            Maximum = maximum;
            MaximumMagnitudeX = Math.Max(Math.Abs((double)minimum.X), Math.Abs((double)maximum.X));
            MaximumMagnitudeY = Math.Max(Math.Abs((double)minimum.Y), Math.Abs((double)maximum.Y));
            MaximumMagnitudeZ = Math.Max(Math.Abs((double)minimum.Z), Math.Abs((double)maximum.Z));
            Surface = surface;
            Escape = escape;
        }
    }

    internal LightingRayHierarchy(IReadOnlyList<MapRenderSurface> surfaces, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _surfaceNodes = new int[surfaces.Count];
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
                _surfaceNodes[indices[start]] = node;
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

    internal WebGpuRayTraversal? CreateGpuTraversal(CancellationToken cancellationToken)
    {
        var nodes = new WebGpuRayTraversal.BoundsNode[_nodes.Length];
        for (int index = 0; index < nodes.Length; index++)
        {
            ref readonly Node node = ref _nodes[index];
            nodes[index] = new(node.Minimum, node.Escape, node.Maximum, node.Surface);
        }
        return WebGpuRayTraversal.TryCreate(nodes, cancellationToken);
    }

    // Escape indices skip whole disjoint subtrees without a traversal stack or heap allocation.
    internal struct RayEnumerator
    {
        private readonly LightingRayHierarchy _hierarchy;
        private readonly double _originX;
        private readonly double _originY;
        private readonly double _originZ;
        private readonly double _absoluteOriginX;
        private readonly double _absoluteOriginY;
        private readonly double _absoluteOriginZ;
        private readonly double _directionX;
        private readonly double _directionY;
        private readonly double _directionZ;
        private readonly double _initialExit;
        private readonly CancellationToken _cancellationToken;
        private int _cursor;
        private int _visited;

        internal RayEnumerator(LightingRayHierarchy hierarchy, Vector3 origin, Vector3 direction,
            double maximumDistance, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hierarchy = hierarchy;
            _originX = origin.X;
            _originY = origin.Y;
            _originZ = origin.Z;
            _absoluteOriginX = Math.Abs((double)origin.X);
            _absoluteOriginY = Math.Abs((double)origin.Y);
            _absoluteOriginZ = Math.Abs((double)origin.Z);
            _directionX = direction.X;
            _directionY = direction.Y;
            _directionZ = direction.Z;
            _initialExit = maximumDistance + Math.Abs(maximumDistance) * BoundsRoundoff + 0.001;
            _cancellationToken = cancellationToken;
            _cursor = 0;
            _visited = 0;
            Current = 0;
        }

        internal int Current { get; private set; }

        internal readonly bool IntersectsSurface(int surface)
        {
            ref readonly Node node = ref _hierarchy._nodes[_hierarchy._surfaceNodes[surface]];
            return IntersectsBounds(in node);
        }

        internal bool MoveNext()
        {
            while (_cursor < _hierarchy._nodes.Length)
            {
                if ((_visited++ & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
                ref readonly Node node = ref _hierarchy._nodes[_cursor];
                if (!IntersectsBounds(in node))
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

        private readonly bool IntersectsBounds(in Node bounds)
        {
            // Account for float triangle arithmetic at the origin and finite light endpoint.
            // Expanding this broadphase never changes the exact distance/contact rejection.
            double enter = 0, exit = _initialExit;
            Vector3 minimumBounds = bounds.Minimum, maximumBounds = bounds.Maximum;
            if (!IntersectsBoundsAxis(minimumBounds.X, maximumBounds.X, bounds.MaximumMagnitudeX,
                    _originX, _absoluteOriginX, _directionX, ref enter, ref exit)) return false;
            if (!IntersectsBoundsAxis(minimumBounds.Y, maximumBounds.Y, bounds.MaximumMagnitudeY,
                    _originY, _absoluteOriginY, _directionY, ref enter, ref exit)) return false;
            return IntersectsBoundsAxis(minimumBounds.Z, maximumBounds.Z, bounds.MaximumMagnitudeZ,
                _originZ, _absoluteOriginZ, _directionZ, ref enter, ref exit);
        }
    }

    private static bool IntersectsBoundsAxis(float minimum, float maximum, double maximumMagnitude,
        double origin, double absoluteOrigin, double direction, ref double enter, ref double exit)
    {
        double padding = (absoluteOrigin + maximumMagnitude) * BoundsRoundoff;
        double paddedMinimum = minimum - padding, paddedMaximum = maximum + padding;
        if (direction == 0)
        {
            if (origin < paddedMinimum || origin > paddedMaximum) return false;
            return true;
        }
        double first = (paddedMinimum - origin) / direction;
        double second = (paddedMaximum - origin) / direction;
        enter = Math.Max(enter, Math.BitDecrement(Math.Min(first, second)));
        exit = Math.Min(exit, Math.BitIncrement(Math.Max(first, second)));
        if (enter > exit) return false;
        return true;
    }
}
