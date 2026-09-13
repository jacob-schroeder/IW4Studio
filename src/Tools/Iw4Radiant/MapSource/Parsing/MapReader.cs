using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.MapSource.Parsing;

internal sealed class MapReader
{
    private readonly string _source;
    private readonly List<MapToken> _tokens;
    private int _position;

    public MapReader(string source)
    {
        _source = source;
        _tokens = MapTokenizer.Tokenize(source);
    }

    public MapDocument Read()
    {
        Expect("iwmap");
        MapToken version = Take();
        if (version.Quoted || version.Value != "4" || version.LogicalLine != _tokens[0].LogicalLine)
            throw Error(version, "Only native iwmap 4 source maps are supported.");
        var document = new MapDocument();
        document.Header.Clear();
        while (HasToken && (Current.Quoted || Current.Value != "{"))
            ReadLine();
        if (!HasToken)
            throw Error(version, "Expected a worldspawn entity.");
        string header = _source[..Current.Start].TrimEnd();
        document.Header.AddRange(header.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        while (HasToken)
            document.Entities.Add(ReadEntity());
        if (document.Entities[0].ClassName != "worldspawn" ||
            document.Entities.Count(entity => entity.ClassName == "worldspawn") != 1)
            throw Error(_tokens[0], "A map must begin with exactly one worldspawn entity.");
        return document;
    }

    private MapEntity ReadEntity()
    {
        Expect("{");
        var entity = new MapEntity();
        while (HasToken && (Current.Quoted || Current.Value != "}"))
        {
            if (!Current.Quoted && Current.Value == "{")
            {
                ReadPrimitive(entity);
            }
            else if (Current.Quoted)
            {
                MapToken key = Take();
                MapToken value = Take();
                if (!value.Quoted || value.LogicalLine != key.LogicalLine)
                    throw Error(value, "Expected a quoted entity property value on the same line.");
                if (!entity.Properties.TryAdd(key.Value, value.Value))
                    throw Error(key, $"Duplicate entity property '{key.Value}' cannot be represented without loss.");
                if (HasToken && Current.LogicalLine == key.LogicalLine && Current.Value != "}")
                    throw Error(Current, "Unexpected text after an entity property.");
            }
            else
            {
                entity.Directives.Add(ReadLine());
            }
        }
        Expect("}");
        return entity;
    }

    private void ReadPrimitive(MapEntity entity)
    {
        MapToken open = Expect("{");
        int begin = _position;
        int depth = 1;
        bool nested = false;
        while (HasToken && depth != 0)
        {
            MapToken token = Take();
            if (!token.Quoted && token.Value == "{")
            {
                depth++;
                nested = true;
            }
            if (!token.Quoted && token.Value == "}") depth--;
        }
        if (depth != 0)
            throw Error(open, "Unterminated primitive block.");
        int end = _position - 1;
        int resume = _position;
        string raw = _source[open.Start.._tokens[end].End];
        _position = begin;
        if (_position == end)
            throw Error(open, "Empty primitive block.");
        if (Current.Value == "mesh")
        {
            MapTerrain? terrain = ReadTerrain(end);
            if (terrain is null)
                entity.PreservedPrimitives.Add(raw);
            else
                entity.Terrains.Add(terrain);
        }
        else if (nested)
        {
            entity.PreservedPrimitives.Add(raw);
        }
        else
        {
            var brush = new MapBrush();
            while (_position < end)
            {
                if (Current.Value == "(")
                    brush.Faces.Add(ReadFace());
                else
                    brush.Directives.Add(ReadLine());
            }
            if (brush.Faces.Count < 4)
                throw Error(open, "A convex brush must contain at least four planes.");
            entity.Brushes.Add(brush);
        }
        _position = resume;
    }

    private MapFace ReadFace()
    {
        MapToken start = Current;
        var face = new MapFace { A = ReadPoint(start), B = ReadPoint(start), C = ReadPoint(start) };
        Vector3 normal = face.Normal;
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z) || normal == Vector3.Zero)
            throw Error(start, "A brush plane needs three distinct, non-collinear points within the supported coordinate range.");
        MapToken material = TakeOnLine(start);
        RequireName(material);
        face.Material = material.Value;
        int projectionStart = HasToken ? Current.Start : material.End;
        for (int index = 0; index < 6; index++)
            ReadFloat(start);
        RequireName(TakeOnLine(start));
        for (int index = 0; index < 6; index++)
            ReadFloat(start);
        // Smoothing and any additional face attributes remain attached to this face.
        while (HasToken && Current.LogicalLine == start.LogicalLine)
        {
            if (Current.Value is "{" or "}" or "(" or ")")
                throw Error(Current, "Unexpected delimiter after a brush plane.");
            Take();
        }
        face.Projection = ThroughNextToken(projectionStart);
        return face;
    }

    private MapTerrain? ReadTerrain(int primitiveEnd)
    {
        Expect("mesh");
        Expect("{");
        var terrain = new MapTerrain();
        while (HasToken && Current.Value is "layer" or "contents" or "toolFlags")
            terrain.Directives.Add(ReadLine());
        MapToken material = Take();
        RequireName(material);
        terrain.Material = material.Value;
        MapToken lightmap = Take();
        RequireName(lightmap);
        terrain.Lightmap = lightmap.Value;
        if (HasToken && Current.Value == "smoothing")
        {
            MapToken smoothing = Take();
            MapToken name = TakeOnLine(smoothing);
            RequireName(name);
            terrain.Smoothing = name.Value;
            if (HasToken && Current.LogicalLine == smoothing.LogicalLine)
                return null;
        }
        MapToken dimensions = Current;
        if (!int.TryParse(dimensions.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return null;
        terrain.Width = ReadInt(dimensions);
        terrain.Height = ReadInt(dimensions);
        terrain.LightmapSize = ReadFloat(dimensions);
        terrain.Subdivision = ReadInt(dimensions);
        if (HasToken && Current.LogicalLine == dimensions.LogicalLine && Current.Value != "(")
            return null;
        long count = (long)terrain.Width * terrain.Height;
        if (terrain.Width < 2 || terrain.Height < 2 || count > (primitiveEnd - _position) / 9)
            throw Error(dimensions, "Terrain dimensions do not match a complete vertex grid.");
        int vertexCount = (int)count;
        terrain.Vertices = new Vector3[vertexCount];
        terrain.TextureCoordinates = new Vector2[vertexCount];
        terrain.LightmapCoordinates = new Vector2[vertexCount];
        terrain.Colors = new Vector4[vertexCount];
        terrain.EdgeFlags = new int[vertexCount];
        for (int column = 0; column < terrain.Width; column++)
        {
            Expect("(");
            for (int row = 0; row < terrain.Height; row++)
            {
                MapToken vertex = Expect("v");
                int index = column * terrain.Height + row;
                terrain.Vertices[index] = new Vector3(ReadFloat(vertex), ReadFloat(vertex), ReadFloat(vertex));
                Vector4 color = Vector4.One;
                if (HasToken && Current.Value == "c")
                {
                    MapToken colorToken = Take();
                    float blue = ReadColor(colorToken), green = ReadColor(colorToken), red = ReadColor(colorToken);
                    color = new Vector4(red, green, blue, ReadColor(colorToken));
                }
                terrain.Colors[index] = color;
                if (HasToken && Current.Value != "t" && Current.LogicalLine == vertex.LogicalLine)
                    return null;
                MapToken texture = Expect("t");
                terrain.TextureCoordinates[index] = new Vector2(ReadFloat(texture), ReadFloat(texture)) / 1024;
                terrain.LightmapCoordinates[index] = new Vector2(ReadFloat(texture), ReadFloat(texture)) / 1024;
                if (HasToken && Current.Value == "f")
                {
                    MapToken flags = Take();
                    terrain.EdgeFlags[index] = ReadInt(flags);
                }
                if (HasToken && Current.Value is not "v" and not ")" and not "}")
                    return null;
            }
            Expect(")");
        }
        if (HasToken && Current.Value != "}")
            return null;
        Expect("}");
        return _position == primitiveEnd ? terrain : null;
    }

    private float ReadColor(MapToken line)
    {
        MapToken token = Current;
        int value = ReadInt(line);
        if (value is < 0 or > 255)
            throw Error(token, "Terrain vertex colors must be bytes between 0 and 255.");
        return value / 255f;
    }

    private Vector3 ReadPoint(MapToken line)
    {
        ExpectOnLine("(", line);
        var point = new Vector3(ReadFloat(line), ReadFloat(line), ReadFloat(line));
        ExpectOnLine(")", line);
        return point;
    }

    private float ReadFloat(MapToken line)
    {
        MapToken token = TakeOnLine(line);
        if (!float.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw Error(token, $"Expected a finite number, found '{token.Value}'.");
        return value;
    }

    private int ReadInt(MapToken line)
    {
        MapToken token = TakeOnLine(line);
        if (!int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw Error(token, $"Expected an integer, found '{token.Value}'.");
        return value;
    }

    private static void RequireName(MapToken token)
    {
        if (string.IsNullOrWhiteSpace(token.Value) || token.Value is "{" or "}" or "(" or ")" or ";")
            throw Error(token, "Expected a material name.");
    }

    private string ReadLine()
    {
        MapToken first = Current;
        while (HasToken && Current.LogicalLine == first.LogicalLine)
        {
            if (!Current.Quoted && Current.Value is "{" or "}" or "(" or ")")
                throw Error(Current, "Unexpected delimiter in a map directive.");
            Take();
        }
        return ThroughNextToken(first.Start);
    }

    // Include complete trailing comments instead of truncating them at a physical line break.
    private string ThroughNextToken(int start) =>
        _source[start..(HasToken ? Current.Start : _source.Length)].TrimEnd();

    private bool HasToken => _position < _tokens.Count;
    private MapToken Current => HasToken ? _tokens[_position] :
        throw new FormatException($"Line {(_tokens.Count == 0 ? 1 : _tokens[^1].Line)}: Unexpected end of map.");
    private MapToken Take()
    {
        MapToken token = Current;
        _position++;
        return token;
    }

    private MapToken Expect(string value)
    {
        MapToken token = Take();
        if (token.Quoted || token.Value != value)
            throw Error(token, $"Expected '{value}', found '{token.Value}'.");
        return token;
    }

    private MapToken TakeOnLine(MapToken line)
    {
        MapToken token = Take();
        if (token.LogicalLine != line.LogicalLine)
            throw Error(token, $"Expected more values on line {line.Line}.");
        return token;
    }

    private void ExpectOnLine(string value, MapToken line)
    {
        MapToken token = Expect(value);
        if (token.LogicalLine != line.LogicalLine)
            throw Error(token, $"Expected '{value}' on line {line.Line}.");
    }

    private static FormatException Error(MapToken token, string message) => new($"Line {token.Line}: {message}");
}
