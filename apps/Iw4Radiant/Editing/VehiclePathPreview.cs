using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

// A read-only view of authored vehicle links. The game resolves missing speed and
// lookahead values at runtime; the editor only previews values present on the node.
internal sealed class VehiclePathPreview
{
    private readonly Dictionary<MapEntity, Node> _byEntity = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, List<Node>> _incoming = [];

    internal IReadOnlyList<Node> Nodes { get; }

    internal VehiclePathPreview(MapDocument document)
    {
        Nodes = document.Entities.Where(IsNode).Select(entity => new Node(entity)).ToArray();
        foreach (Node node in Nodes)
        {
            _byEntity.Add(node.Entity, node);
            _incoming.Add(node, []);
        }

        var names = Nodes.Where(node => node.Name.Length > 0)
            .GroupBy(node => node.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (Node node in Nodes)
        {
            if (node.Name.Length == 0) node.Warnings.Add("Name is missing; this node cannot be linked by another node.");
            if (!node.HasPosition) node.Warnings.Add("Origin is missing or invalid.");
            if (node.Name.Length == 0 || node.Target.Length == 0) continue;
            if (!names.TryGetValue(node.Target, out Node[]? matches))
                node.Warnings.Add($"Target '{node.Target}' does not match a vehicle node.");
            else if (matches.Length != 1)
                node.Warnings.Add($"Target '{node.Target}' matches {matches.Length} vehicle nodes.");
            else
            {
                node.Next = matches[0];
                _incoming[matches[0]].Add(node);
            }
        }
        MarkCycles();
    }

    internal static bool IsNode(MapEntity entity) =>
        entity.ClassName is "info_vehicle_node" or "info_vehicle_node_rotate";

    internal Node? Find(MapEntity entity) => _byEntity.GetValueOrDefault(entity);

    internal HashSet<Node> ConnectedTo(Node selected)
    {
        var result = new HashSet<Node> { selected };
        var queue = new Queue<Node>();
        queue.Enqueue(selected);
        while (queue.Count > 0)
        {
            Node current = queue.Dequeue();
            if (current.Next is { } next && result.Add(next)) queue.Enqueue(next);
            foreach (Node source in _incoming[current])
                if (result.Add(source)) queue.Enqueue(source);
        }
        return result;
    }

    internal IReadOnlyList<(Vector3 Start, Vector3 End)> LookaheadSegments(Node node)
    {
        if (node.SpeedMph is not > 0 || node.LookaheadSeconds is not > 0 || !node.HasPosition) return [];
        var seen = new HashSet<Node>();
        for (Node? candidate = node; candidate is not null && seen.Add(candidate); candidate = candidate.Next)
            if (candidate.Cycle) return []; // A looping path has no unambiguous preview endpoint.

        double remaining = node.SpeedMph.Value * 17.6d * node.LookaheadSeconds.Value;
        var segments = new List<(Vector3 Start, Vector3 End)>();
        Node? current = node;
        while (remaining > 0 && current is { Next: { } next })
        {
            if (!current.HasPosition || !next.HasPosition) break;
            float length = Vector3.Distance(current.Position, next.Position);
            if (!float.IsFinite(length)) break;
            if (length < 0.001f) { current = next; continue; }
            Vector3 end = remaining < length
                ? Vector3.Lerp(current.Position, next.Position, (float)(remaining / length))
                : next.Position;
            segments.Add((current.Position, end));
            remaining -= length;
            current = next;
        }
        return segments;
    }

    private void MarkCycles()
    {
        var visited = new HashSet<Node>();
        foreach (Node start in Nodes)
        {
            if (visited.Contains(start)) continue;
            var route = new List<Node>();
            var positions = new Dictionary<Node, int>();
            for (Node? current = start; current is not null && !visited.Contains(current); current = current.Next)
            {
                if (positions.TryGetValue(current, out int first))
                {
                    for (int index = first; index < route.Count; index++) route[index].Cycle = true;
                    break;
                }
                positions.Add(current, route.Count);
                route.Add(current);
            }
            foreach (Node node in route) visited.Add(node);
        }
    }

    internal sealed class Node
    {
        internal MapEntity Entity { get; }
        internal string Name { get; }
        internal string Target { get; }
        internal Vector3 Position { get; }
        internal bool HasPosition { get; }
        internal float? SpeedMph { get; }
        internal float? LookaheadSeconds { get; }
        internal Vector3? AuthoredHeading { get; }
        internal Node? Next { get; set; }
        internal bool Cycle { get; set; }
        internal List<string> Warnings { get; } = [];

        internal Node(MapEntity entity)
        {
            Entity = entity;
            Name = entity.Properties.GetValueOrDefault("targetname", "");
            Target = entity.Properties.GetValueOrDefault("target", "");
            HasPosition = entity.TryGetOrigin(out Vector3 position);
            Position = position;
            SpeedMph = ReadPositive("speed", "Speed");
            LookaheadSeconds = ReadPositive("lookahead", "Lookahead");
            if (entity.Properties.ContainsKey("angles") || entity.Properties.ContainsKey("angle"))
            {
                try { AuthoredHeading = EntityOrientation.Forward(EntityOrientation.Read(entity)); }
                catch (ArgumentException) { Warnings.Add("Heading angles are invalid."); }
            }

            float? ReadPositive(string key, string label)
            {
                if (!entity.Properties.TryGetValue(key, out string? text) || text.Length == 0) return null;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                    float.IsFinite(value) && value > 0) return value;
                Warnings.Add($"{label} must be a positive number.");
                return null;
            }
        }

        internal Vector3? Heading => AuthoredHeading ?? (Next is { HasPosition: true } next && HasPosition &&
            Vector3.DistanceSquared(Position, next.Position) > 0.000001f
                ? Vector3.Normalize(next.Position - Position) : null);
    }
}
