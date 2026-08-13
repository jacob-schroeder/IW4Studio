using System.Globalization;
using System.Numerics;

namespace IW4.Assets.XModel.Export;

/// <summary>One source-location diagnostic emitted while reading XMODEL_EXPORT.</summary>
public sealed record XModelExportParseIssue(int Line, int Column, string Message);

/// <summary>Strict reader for the version-6 XMODEL_EXPORT handoff grammar.</summary>
public static class XModelExportReader
{
    public static bool TryRead(
        TextReader reader,
        out XModelExportDocument? document,
        out IReadOnlyList<XModelExportParseIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var parser = new Parser(reader.ReadToEnd());
        document = parser.Read();
        issues = parser.Issues;
        return document is not null && issues.Count == 0;
    }

    private sealed class Parser
    {
        private readonly string[] _lines;
        private int _line;
        private readonly List<XModelExportParseIssue> _issues = [];
        internal Parser(string text) => _lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        internal IReadOnlyList<XModelExportParseIssue> Issues => Array.AsReadOnly(_issues.ToArray());

        internal XModelExportDocument? Read()
        {
            try
            {
                Expect("MODEL"); Expect("VERSION", "6");
                int boneCount = Count("NUMBONES");
                var headers = new (int Parent, string Name)[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    string[] p = Parts(Next()); Require(p.Length == 4 && p[0] == "BONE" && Integer(p[1]) == i, "Expected indexed BONE header.");
                    int parent = Integer(p[2]); Require(parent >= -1 && parent < i && ValidString(p[3], false), "Bone parent or name is invalid."); headers[i] = (parent, p[3]);
                }
                var bones = new List<XModelExportBone>(boneCount);
                for (int i = 0; i < boneCount; i++)
                {
                    Expect("BONE", i.ToString(CultureInfo.InvariantCulture));
                    Vector3 offset = Vec3(ExpectValue("OFFSET"));
                    Vector3 scale = Vec3(ExpectValue("SCALE"));
                    Require(Vector3.DistanceSquared(scale, Vector3.One) <= 0.000001f, "Only unit bone scale is representable.");
                    Vector3 x = Vec3(ExpectValue("X")); Vector3 y = Vec3(ExpectValue("Y")); Vector3 z = Vec3(ExpectValue("Z"));
                    Require(MathF.Abs(Vector3.Dot(x, y)) < .001f && MathF.Abs(Vector3.Dot(x, z)) < .001f && MathF.Abs(Vector3.Dot(y, z)) < .001f && MathF.Abs(x.LengthSquared() - 1) < .001f && MathF.Abs(y.LengthSquared() - 1) < .001f && MathF.Abs(z.LengthSquared() - 1) < .001f && Vector3.Dot(Vector3.Cross(x, y), z) > .999f, "Bone rotation axes must be a right-handed orthonormal basis.");
                    var matrix = new Matrix4x4(x.X, y.X, z.X, 0, x.Y, y.Y, z.Y, 0, x.Z, y.Z, z.Z, 0, 0, 0, 0, 1);
                    Quaternion q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
                    bones.Add(new(headers[i].Name, headers[i].Parent, offset, q));
                }
                int vertexCount = Count("NUMVERTS"); var vertices = new List<XModelExportVertex>(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    Expect("VERT", i.ToString(CultureInfo.InvariantCulture)); Vector3 position = Vec3(ExpectValue("OFFSET"));
                    int weightsCount = Count("BONES"); Require(weightsCount > 0, "Vertex must have at least one bone weight.");
                    var weights = new List<XModelExportBoneWeight>(weightsCount); float total = 0;
                    for (int j = 0; j < weightsCount; j++) { string[] p = Parts(Next()); Require(p.Length == 3 && p[0] == "BONE", "Expected vertex BONE weight."); int b = Integer(p[1]); float w = Float(p[2]); Require(b >= 0 && b < boneCount && w >= 0, "Vertex bone weight is invalid."); weights.Add(new(b, w)); total += w; }
                    Require(float.IsFinite(total) && MathF.Abs(total - 1) <= .00001f, "Vertex weights must sum to one."); vertices.Add(new(position, Array.AsReadOnly(weights.ToArray())));
                }
                int faceCount = Count("NUMFACES"); var triangles = new List<XModelExportTriangle>(faceCount);
                for (int i = 0; i < faceCount; i++)
                {
                    string[] p = Parts(Next()); Require(p.Length == 5 && p[0] == "TRI" && Integer(p[3]) == 0 && Integer(p[4]) == 0, "Expected TRI object material 0 0.");
                    int obj = Integer(p[1]); int material = Integer(p[2]); var first = Corner(vertexCount); var second = Corner(vertexCount); var third = Corner(vertexCount);
                    Require(
                        first.VertexIndex != second.VertexIndex &&
                        first.VertexIndex != third.VertexIndex &&
                        second.VertexIndex != third.VertexIndex &&
                        Vector3.Cross(
                            vertices[second.VertexIndex].Position - vertices[first.VertexIndex].Position,
                            vertices[third.VertexIndex].Position - vertices[first.VertexIndex].Position) != Vector3.Zero,
                        "Triangle is degenerate.");
                    triangles.Add(new(obj, material, first, second, third));
                }
                int objectCount = Count("NUMOBJECTS"); var objects = new List<XModelExportObject>(objectCount);
                for (int i = 0; i < objectCount; i++) { string[] p = Parts(Next()); Require(p.Length == 3 && p[0] == "OBJECT" && Integer(p[1]) == i && ValidString(p[2], false), "Expected indexed OBJECT."); objects.Add(new(p[2])); }
                int materialCount = Count("NUMMATERIALS"); var materials = new List<XModelExportMaterial>(materialCount);
                for (int i = 0; i < materialCount; i++)
                {
                    string[] p = Parts(Next()); Require(p.Length == 5 && p[0] == "MATERIAL" && Integer(p[1]) == i, "Expected indexed MATERIAL.");
                    Require(ValidString(p[2], false) && ValidString(p[3], false) && ValidString(p[4], true), "Material strings are invalid."); materials.Add(new(p[2], p[4]));
                    ExpectMaterialProperties();
                }
                Require(triangles.All(t => t.ObjectIndex >= 0 && t.ObjectIndex < objectCount && t.MaterialIndex >= 0 && t.MaterialIndex < materialCount), "Triangle has an incomplete object or material reference.");
                while (_line < _lines.Length) { string trailing = _lines[_line++].Trim(); Require(trailing.Length == 0 || trailing.StartsWith("//", StringComparison.Ordinal), "Unexpected trailing content."); }
                return new(Array.AsReadOnly(bones.ToArray()), Array.AsReadOnly(vertices.ToArray()), Array.AsReadOnly(triangles.ToArray()), Array.AsReadOnly(objects.ToArray()), Array.AsReadOnly(materials.ToArray()));
            }
            catch (InvalidDataException) { return null; }
        }
        private void ExpectMaterialProperties()
        {
            (string Name, int ValueCount)[] properties =
            [
                ("COLOR", 4),
                ("TRANSPARENCY", 4),
                ("AMBIENTCOLOR", 4),
                ("INCANDESCENCE", 4),
                ("COEFFS", 2),
                ("GLOW", 2),
                ("REFRACTIVE", 2),
                ("SPECULARCOLOR", 4),
                ("REFLECTIVECOLOR", 4),
                ("REFLECTIVE", 2),
                ("BLINN", 2),
                ("PHONG", 1)
            ];
            foreach ((string name, int valueCount) in properties)
            {
                string[] parts = Parts(Next());
                Require(
                    parts.Length == valueCount + 1 &&
                    parts[0] == name,
                    $"Expected {name} followed by {valueCount} values.");
                for (int index = 1; index < parts.Length; index++)
                    _ = Float(parts[index]);
            }
        }
        private XModelExportCorner Corner(int vertices)
        {
            string[] v = Parts(Next()); Require(v.Length == 2 && v[0] == "VERT", "Expected corner VERT."); int index = Integer(v[1]); Require(index >= 0 && index < vertices, "Corner vertex index is invalid.");
            Vector3 normal = Vec3Space(ExpectValue("NORMAL")); Vector4 color = Vec4Space(ExpectValue("COLOR")); string[] uv = Parts(Next()); Require(uv.Length == 4 && uv[0] == "UV" && uv[1] == "1", "Expected UV 1."); return new(index, normal, color, new(Float(uv[2]), Float(uv[3])));
        }
        private int Count(string name) { string[] p = Parts(Next()); Require(p.Length == 2 && p[0] == name, $"Expected {name} count."); int count = Integer(p[1]); Require(count >= 0, "Count cannot be negative."); return count; }
        private string ExpectValue(string name) { string[] p = Parts(Next()); Require(p.Length >= 2 && p[0] == name, $"Expected {name}."); return string.Join(" ", p.Skip(1)); }
        private void Expect(params string[] expected) { string[] p = Parts(Next()); Require(p.SequenceEqual(expected, StringComparer.Ordinal), $"Expected {string.Join(' ', expected)}."); }
        private string Next() { while (_line < _lines.Length) { string line = _lines[_line++].Trim(); if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal)) return line; } Error("Unexpected end of file."); return string.Empty; }
        private string[] Parts(string text)
        {
            var values = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            bool escaped = false;
            bool tokenStarted = false;
            foreach (char value in text)
            {
                if (escaped)
                {
                    current.Append(value switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'f' => '\f',
                        _ => value
                    });
                    escaped = false;
                    tokenStarted = true;
                    continue;
                }
                if (quoted && value == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (value == '"')
                {
                    quoted = !quoted;
                    tokenStarted = true;
                    continue;
                }
                if (!quoted && char.IsWhiteSpace(value))
                {
                    if (tokenStarted)
                    {
                        values.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }
                    continue;
                }

                current.Append(value);
                tokenStarted = true;
            }

            Require(!quoted && !escaped, "Unterminated quoted value.");
            if (tokenStarted)
                values.Add(current.ToString());
            return values.ToArray();
        }
        private Vector3 Vec3(string values) { string[] p = values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); Require(p.Length == 3, "Expected three vector values."); return new(Float(p[0]), Float(p[1]), Float(p[2])); }
        private Vector3 Vec3Space(string values) { string[] p = values.Split(' ', StringSplitOptions.RemoveEmptyEntries); Require(p.Length == 3, "Expected three vector values."); return new(Float(p[0]), Float(p[1]), Float(p[2])); }
        private Vector4 Vec4Space(string values) { string[] p = values.Split(' ', StringSplitOptions.RemoveEmptyEntries); Require(p.Length == 4, "Expected four vector values."); return new(Float(p[0]), Float(p[1]), Float(p[2]), Float(p[3])); }
        private int Integer(string value) { if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) Error($"Invalid integer '{value}'."); return result; }
        private float Float(string value) { if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result)) Error($"Invalid finite number '{value}'."); return result; }
        private static bool ValidString(string value, bool allowEmpty) => (allowEmpty || value.Length > 0) && !value.Any(char.IsControl);
        private void Require(bool value, string message) { if (!value) Error(message); }
        private void Error(string message) { _issues.Add(new(_line, 1, message)); throw new InvalidDataException(message); }
    }
}
