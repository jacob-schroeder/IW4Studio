using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class TerrainPainting
{
    internal static bool Dab(MapTerrain terrain, Vector2 center, float radius, Vector4 target, float opacity, bool alphaOnly)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(radius) || radius <= 0 ||
            !float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentException("Paint position, radius, and opacity must be finite; radius must be positive and opacity between zero and one.");
        ValidateColor(target);
        if (terrain.Colors.Length != terrain.Vertices.Length)
            throw new ArgumentException("Terrain vertex colors do not match the vertex grid.");
        if (opacity == 0) return false;
        bool changed = false;
        for (int index = 0; index < terrain.Vertices.Length; index++)
        {
            Vector3 point = terrain.Vertices[index];
            double dx = (double)point.X - center.X, dy = (double)point.Y - center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= radius) continue;
            float falloff = (float)(1 - distance / radius);
            Vector4 painted = Blend(terrain.Colors[index], target, opacity * falloff * falloff, alphaOnly);
            if (painted == terrain.Colors[index]) continue;
            terrain.Colors[index] = painted;
            changed = true;
        }
        return changed;
    }

    internal static void Fill(EditorSession session, bool alphaOnly)
    {
        Vector4 target = session.PaintColor with { W = session.PaintAlpha };
        ValidateColor(target);
        var changes = new List<(MapTerrain Terrain, int Index, Vector4 Color)>();
        foreach (var (terrain, indices) in SelectedVertices(session))
        foreach (int index in indices)
        {
            if ((uint)index >= (uint)terrain.Colors.Length)
                throw new ArgumentException("A selected terrain vertex no longer has color data.");
            Vector4 color = Blend(terrain.Colors[index], target, 1, alphaOnly);
            if (color != terrain.Colors[index]) changes.Add((terrain, index, color));
        }
        if (changes.Count == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Terrain.Colors[change.Index] = change.Color;
        });
    }

    internal static void AddOverlay(EditorSession session, Func<string, bool> supportsAlpha)
    {
        if (!supportsAlpha(session.Material))
            throw new ArgumentException("Choose an alpha-blended material with a wc_ technique set in the browser before adding a paintable overlay.");
        MapTerrain[] terrains = session.Selection.Items.OfType<MapTerrain>().Where(terrain => !terrain.IsCurve &&
            session.Visibility.CanSelect(session.Document, terrain)).ToArray();
        if (terrains.Length == 0 || terrains.Length != session.Selection.Count)
            throw new ArgumentException("Select whole terrain meshes to add a matching paintable overlay.");
        var additions = terrains.Select(terrain =>
        {
            MapTerrain overlay = terrain.Clone();
            overlay.Material = session.Material;
            TerrainContents.SetNonColliding(overlay, true);
            for (int index = 0; index < overlay.Colors.Length; index++) overlay.Colors[index].W = 0;
            return (Owner: session.Document.Entities.First(entity => entity.Terrains.Contains(terrain)), Overlay: overlay);
        }).ToArray();
        session.Edit(() =>
        {
            foreach (var addition in additions) addition.Owner.Terrains.Add(addition.Overlay);
            session.Selection.SetRange(additions.Select(addition => addition.Overlay));
            session.SculptMode = TerrainSculptMode.PaintAlpha;
            session.PaintAlpha = 1;
            session.Tool = EditorTool.Sculpt;
        });
    }

    internal static MapTerrain[] SelectedTerrains(EditorSession session) => SelectedVertices(session).Select(item => item.Terrain).ToArray();

    private static IEnumerable<(MapTerrain Terrain, int[] Indices)> SelectedVertices(EditorSession session)
    {
        var selected = new Dictionary<MapTerrain, HashSet<int>>();
        foreach (object item in session.Selection.Items)
        {
            if (item is TerrainVertexSelection vertex) Add(vertex.Terrain, [vertex.Index]);
            else if (item is MapTerrain terrain) Add(terrain, Enumerable.Range(0, terrain.Vertices.Length));
            else if (item is MapEntity entity)
                foreach (MapTerrain patch in entity.Terrains) Add(patch, Enumerable.Range(0, patch.Vertices.Length));
        }
        return selected.Select(pair => (pair.Key, pair.Value.ToArray()));

        void Add(MapTerrain terrain, IEnumerable<int> indices)
        {
            if (!session.Visibility.CanSelect(session.Document, terrain)) return;
            if (!selected.TryGetValue(terrain, out var values)) selected.Add(terrain, values = []);
            values.UnionWith(indices);
        }
    }

    private static Vector4 Blend(Vector4 source, Vector4 target, float amount, bool alphaOnly)
    {
        ValidateColor(source);
        // Native map colors are bytes: making this explicit keeps paint stable across save/reload.
        return alphaOnly ? source with { W = Quantize(source.W + (target.W - source.W) * amount) } :
            new(Quantize(source.X + (target.X - source.X) * amount), Quantize(source.Y + (target.Y - source.Y) * amount),
                Quantize(source.Z + (target.Z - source.Z) * amount), source.W);
    }

    private static float Quantize(float value) => MathF.Round(Math.Clamp(value, 0, 1) * 255) / 255;

    private static void ValidateColor(Vector4 color)
    {
        for (int component = 0; component < 4; component++)
            if (!float.IsFinite(color[component]) || color[component] < 0 || color[component] > 1)
                throw new ArgumentException("Paint color and alpha must be between zero and one.");
    }
}
