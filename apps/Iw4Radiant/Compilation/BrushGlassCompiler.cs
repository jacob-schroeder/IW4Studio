using System.Numerics;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Compilation;

internal static class BrushGlassCompiler
{
    internal static IEnumerable<string> RequiredMaterialNames(MapBrush brush)
    {
        var settings = BrushGlass.Read(brush) ??
            throw new InvalidDataException("Configure this pane with Brush type → Breakable glass before building.");
        var (face, _) = BrushGlass.ReadPane(brush);
        return [ModelMaterialName(face.Face.Material), ModelMaterialName(settings.ShatteredMaterial)];
    }

    private static string ModelMaterialName(string name) =>
        name.StartsWith("w/", StringComparison.Ordinal) ? "m/" + name[2..] :
        name.StartsWith("wc/", StringComparison.Ordinal) ? "m/" + name[3..] : name;

    internal static FxGlassSystem Compile(MapDocument document,
        IReadOnlyDictionary<string, MaterialSource> materials, int renderCellCount)
    {
        MapBrush[] panes = document.World.Brushes.Where(BrushGlass.IsGlass).ToArray();
        if (panes.Length == 0) return new FxGlassSystem();
        // Highrise and Derail reserve 1024 additional pieces and eight geometry
        // entries per runtime piece. Keep that observed allocation profile.
        int capacity = checked(panes.Length + 1024);
        if (capacity > ushort.MaxValue)
            throw new NotSupportedException("The map exceeds the native glass piece capacity.");
        var definitions = new List<FxGlassDef>();
        var definitionIndices = new Dictionary<(string Material, string Shattered, string Physics,
            float HalfThickness, FxVec2 U, FxVec2 V), byte>();
        var initial = new FxGlassInitPieceState[panes.Length];
        var geometry = new List<FxGlassGeometryData>(checked(panes.Length * 4));
        for (int index = 0; index < panes.Length; index++)
        {
            MapBrush brush = panes[index];
            var settings = BrushGlass.Read(brush) ??
                throw new InvalidDataException("Configure this pane with Brush type → Breakable glass before building.");
            var (face, thickness) = BrushGlass.ReadPane(brush);
            MaterialSource intact = ResolveMaterial(face.Face.Material, materials);
            MaterialSource shattered = ResolveMaterial(settings.ShatteredMaterial, materials);
            Vector3 faceCenter = face.Vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point) / 4;
            Vector3 normal = face.Face.Normal;
            Vector3 center = faceCenter - normal * (thickness * 0.5f);
            Vector3 axisX = Vector3.Normalize(face.Vertices[1] - face.Vertices[0]);
            Vector3 axisY = Vector3.Normalize(Vector3.Cross(normal, axisX));
            Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                axisX.X, axisX.Y, axisX.Z, 0,
                axisY.X, axisY.Y, axisY.Z, 0,
                normal.X, normal.Y, normal.Z, 0,
                0, 0, 0, 1)));
            var mapping = SurfaceProjection.Parse(face.Face.Projection).GetMapping(normal);
            // PS3 0x00143278 applies these rows directly to signed packed shorts.
            FxVec2 u = new(Vector3.Dot(mapping.U, axisX) / 32, Vector3.Dot(mapping.U, axisY) / 32);
            FxVec2 v = new(Vector3.Dot(mapping.V, axisX) / 32, Vector3.Dot(mapping.V, axisY) / 32);
            FxVec2 texOrigin = new(
                (float)(BrushGeometry.Dot(mapping.U, faceCenter) + mapping.Offset.X),
                (float)(BrushGeometry.Dot(mapping.V, faceCenter) + mapping.Offset.Y));
            float determinant = u.X * v.Y - u.Y * v.X;
            if (!float.IsFinite(determinant) || determinant == 0 ||
                !float.IsFinite(texOrigin.X) || !float.IsFinite(texOrigin.Y))
                throw new InvalidDataException("A glass pane has an invalid texture projection.");
            var key = (intact.Name, shattered.Name, settings.PhysPreset, thickness * 0.5f, u, v);
            if (!definitionIndices.TryGetValue(key, out byte definitionIndex))
            {
                if (definitions.Count == 256)
                    throw new NotSupportedException("A map supports at most 256 distinct glass material, thickness and texture-mapping combinations.");
                definitionIndex = checked((byte)definitions.Count);
                definitionIndices.Add(key, definitionIndex);
                definitions.Add(new FxGlassDef
                {
                    HalfThickness = thickness * 0.5f, TexVecs = [u, v], Color = uint.MaxValue,
                    Material = new MaterialAsset { Info = new MaterialInfo { Name = intact.Name } },
                    MaterialShattered = new MaterialAsset { Info = new MaterialInfo { Name = shattered.Name } },
                    PhysPreset = new PhysPresetAsset { Name = settings.PhysPreset },
                    // PS3 0x00128480 multiplies these into its minimum material
                    // distance score. Authoring uses a zero material score;
                    // this does not reproduce stock texel-density weighting.
                    InvHighMipRadius = 0, ShatteredInvHighMipRadius = 0
                });
            }
            // The recovered stock rectangles run clockwise in their local frame.
            var packed = face.Vertices.Reverse().Select(point => (
                X: PackCoordinate(BrushGeometry.Dot(point - faceCenter, axisX)),
                Y: PackCoordinate(BrushGeometry.Dot(point - faceCenter, axisY)))).ToArray();
            long areaX2 = 0;
            float radiusSquared = 0;
            for (int vertex = 0; vertex < packed.Length; vertex++)
            {
                var a = packed[vertex];
                var b = packed[(vertex + 1) % packed.Length];
                if (!Half.IsFinite((Half)(a.X * u.X + a.Y * u.Y + texOrigin.X)) ||
                    !Half.IsFinite((Half)(a.X * v.X + a.Y * v.Y + texOrigin.Y)))
                    throw new NotSupportedException("A glass pane's texture coordinates exceed the native half-float range. Increase its texture scale or reduce its shift.");
                if (a == b)
                    throw new NotSupportedException("A glass pane is too small for the native 1/32-unit geometry precision.");
                areaX2 += (long)a.X * b.Y - (long)b.X * a.Y;
                radiusSquared = MathF.Max(radiusSquared, ((float)a.X * a.X + (float)a.Y * a.Y) / 1024);
                geometry.Add(new FxGlassGeometryData((uint)(ushort)a.X << 16 | (ushort)a.Y));
            }
            if (areaX2 >= 0)
                throw new InvalidDataException("A glass pane has degenerate packed geometry.");
            initial[index] = new FxGlassInitPieceState
            {
                Frame = new FxSpatialFrame(new FxQuat(rotation.X, rotation.Y, rotation.Z, rotation.W),
                    new FxVec3(center.X, center.Y, center.Z)),
                Radius = MathF.Sqrt(radiusSquared), TexCoordOrigin = texOrigin,
                SupportMask = 0xf0000000, AreaX2 = -areaX2 / 1024f,
                DefIndex = definitionIndex, VertCount = 4
            };
        }
        int words = (capacity + 31) / 32;
        int cellCount = checked(renderCellCount + 1);
        int geometryCapacity = checked(capacity * 8);
        return new FxGlassSystem
        {
            DefCount = (uint)definitions.Count, Defs = definitions,
            PieceLimit = (uint)capacity, PieceWordCount = (uint)words,
            InitPieceCount = (uint)initial.Length, InitPieceStates = initial,
            CellCount = (uint)cellCount, FirstFreePiece = 0xffff0000,
            GeoDataLimit = (uint)geometryCapacity, InitGeoDataCount = (uint)geometry.Count,
            InitGeoData = geometry, LightingHandles = new ushort[initial.Length],
            PiecePlaces = Enumerable.Range(0, capacity).Select(_ => new FxGlassPiecePlace(default, 0, 0)).ToArray(),
            PieceStates = Enumerable.Range(0, capacity).Select(_ => new FxGlassPieceState { Pad11 = new byte[5] }).ToArray(),
            PieceDynamics = Enumerable.Range(0, capacity).Select(_ => new FxGlassPieceDynamics(0, 0, 0, default, default)).ToArray(),
            GeoData = new FxGlassGeometryData[geometryCapacity], IsInUse = new uint[words],
            CellBits = new uint[checked(cellCount * words)], VisData = new byte[(capacity + 15) & ~15],
            LinkOrg = new FxVec3[capacity], HalfThickness = new float[(capacity + 3) & ~3]
        };
    }

    private static short PackCoordinate(double coordinate)
    {
        double packed = Math.Round(coordinate * 32, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(packed) || packed < short.MinValue || packed > short.MaxValue)
            throw new NotSupportedException("A glass pane exceeds the native local coordinate range. Split it into smaller panes.");
        return (short)packed;
    }

    private static MaterialSource ResolveMaterial(string name, IReadOnlyDictionary<string, MaterialSource> materials)
    {
        string modelName = ModelMaterialName(name);
        if (!materials.TryGetValue(modelName, out MaterialSource? material) ||
            !material.PreviewDefinitionAvailable || !File.Exists(material.ImagePath))
            throw new InvalidDataException($"Breakable glass needs material '{modelName}' in the asset library. Load its native material and image before building.");
        if (material.IsSky || material.IsWater || !material.TechniqueSet.StartsWith("m_", StringComparison.Ordinal))
            throw new NotSupportedException($"Material '{modelName}' is not a supported native model material for breakable glass.");
        return material;
    }
}
