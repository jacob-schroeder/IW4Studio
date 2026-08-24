namespace IW4.Assets.D3dbsp;

public static class D3dbspLumpFacts
{
    public static int? GetV22ElementSize(D3dbspLumpType type) => type switch
    {
        D3dbspLumpType.Materials => 72,
        D3dbspLumpType.LightBytes => 3 * 1024 * 1024,
        D3dbspLumpType.LightGridEntries => 4,
        D3dbspLumpType.LightGridColors => 168,
        D3dbspLumpType.Planes => 16,
        D3dbspLumpType.BrushSides => 8,
        D3dbspLumpType.BrushSideEdgeCounts => 1,
        D3dbspLumpType.BrushEdges => 1,
        D3dbspLumpType.Brushes => 4,
        D3dbspLumpType.Triangles or
            D3dbspLumpType.UnlayeredTriangles => 24,
        D3dbspLumpType.DrawVerts or
            D3dbspLumpType.UnlayeredDrawVerts => 68,
        D3dbspLumpType.DrawIndices or
            D3dbspLumpType.UnlayeredDrawIndices => 2,
        D3dbspLumpType.CullGroups or
            D3dbspLumpType.UnlayeredCullGroups => 32,
        D3dbspLumpType.CullGroupIndices => 4,
        D3dbspLumpType.PortalVerts => 12,
        D3dbspLumpType.AabbTrees or
            D3dbspLumpType.UnlayeredAabbTrees => 12,
        D3dbspLumpType.Cells => 112,
        D3dbspLumpType.Portals => 16,
        D3dbspLumpType.Nodes => 36,
        D3dbspLumpType.Leafs => 24,
        D3dbspLumpType.LeafBrushes or
            D3dbspLumpType.LeafSurfaces => 4,
        D3dbspLumpType.CollisionVerts => 12,
        D3dbspLumpType.CollisionTris => 6,
        D3dbspLumpType.CollisionEdgeWalkable => 1,
        D3dbspLumpType.CollisionBorders => 28,
        D3dbspLumpType.CollisionPartitions => 12,
        D3dbspLumpType.CollisionAabbs => 32,
        D3dbspLumpType.Models => 48,
        D3dbspLumpType.Entities => 1,
        D3dbspLumpType.ReflectionProbes => 131_140,
        D3dbspLumpType.VertexLayerData => 1,
        D3dbspLumpType.PrimaryLights => 128,
        D3dbspLumpType.LightGridRows => 1,
        D3dbspLumpType.LightRegions => 1,
        D3dbspLumpType.LightRegionHulls => 76,
        D3dbspLumpType.LightRegionAxes => 20,
        _ => null
    };
}
