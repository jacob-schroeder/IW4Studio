using System.Globalization;
using System.Text;

namespace MapConverter.Game.IW3.PC.Techniques;

/// <summary>
/// Reads the IW3 technique-set and technique text emitted by OpenAssetTools.
/// The result intentionally remains source-side: OpenAssetTools does not recover the
/// original state maps, and its Direct3D shader programs still require a
/// target-specific shader lowering step before an IW4 technique can be linked.
/// </summary>
internal sealed class Iw3TechniqueFormatParser
{
    internal const int TechniqueSlotCount = 34;

    private static readonly (Iw3TechniqueSlot Slot, string DisplayName)[] SlotDefinitions =
    [
        (Iw3TechniqueSlot.DepthPrepass, "depth prepass"),
        (Iw3TechniqueSlot.BuildFloatZ, "build floatz"),
        (Iw3TechniqueSlot.BuildShadowmapDepth, "build shadowmap depth"),
        (Iw3TechniqueSlot.BuildShadowmapColor, "build shadowmap color"),
        (Iw3TechniqueSlot.Unlit, "unlit"),
        (Iw3TechniqueSlot.Emissive, "emissive"),
        (Iw3TechniqueSlot.EmissiveShadow, "emissive shadow"),
        (Iw3TechniqueSlot.Lit, "lit"),
        (Iw3TechniqueSlot.LitSun, "lit sun"),
        (Iw3TechniqueSlot.LitSunShadow, "lit sun shadow"),
        (Iw3TechniqueSlot.LitSpot, "lit spot"),
        (Iw3TechniqueSlot.LitSpotShadow, "lit spot shadow"),
        (Iw3TechniqueSlot.LitOmni, "lit omni"),
        (Iw3TechniqueSlot.LitOmniShadow, "lit omni shadow"),
        (Iw3TechniqueSlot.LitInstanced, "lit instanced"),
        (Iw3TechniqueSlot.LitInstancedSun, "lit instanced sun"),
        (Iw3TechniqueSlot.LitInstancedSunShadow, "lit instanced sun shadow"),
        (Iw3TechniqueSlot.LitInstancedSpot, "lit instanced spot"),
        (Iw3TechniqueSlot.LitInstancedSpotShadow, "lit instanced spot shadow"),
        (Iw3TechniqueSlot.LitInstancedOmni, "lit instanced omni"),
        (Iw3TechniqueSlot.LitInstancedOmniShadow, "lit instanced omni shadow"),
        (Iw3TechniqueSlot.LightSpot, "light spot"),
        (Iw3TechniqueSlot.LightOmni, "light omni"),
        (Iw3TechniqueSlot.LightSpotShadow, "light spot shadow"),
        (Iw3TechniqueSlot.FakeLightNormal, "fakelight normal"),
        (Iw3TechniqueSlot.FakeLightView, "fakelight view"),
        (Iw3TechniqueSlot.SunlightPreview, "sunlight preview"),
        (Iw3TechniqueSlot.CaseTexture, "case texture"),
        (Iw3TechniqueSlot.WireframeSolid, "solid wireframe"),
        (Iw3TechniqueSlot.WireframeShaded, "shaded wireframe"),
        (Iw3TechniqueSlot.ShadowCookieCaster, "shadowcookie caster"),
        (Iw3TechniqueSlot.ShadowCookieReceiver, "shadowcookie receiver"),
        (Iw3TechniqueSlot.DebugBumpmap, "debug bumpmap"),
        (Iw3TechniqueSlot.DebugBumpmapInstanced, "debug bumpmap instanced"),
    ];

    private static readonly IReadOnlyDictionary<string, Iw3TechniqueSlot> SlotsByDisplayName =
        SlotDefinitions.ToDictionary(
            definition => definition.DisplayName,
            definition => definition.Slot,
            StringComparer.Ordinal);

    private readonly Dictionary<string, Iw3TechniqueSource> _techniqueCache =
        new(StringComparer.Ordinal);
    private readonly string _techniqueDirectory;
    private readonly string _techniqueDirectoryPrefix;

    internal Iw3TechniqueFormatParser(string techniqueDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueDirectory);

        _techniqueDirectory = Path.GetFullPath(techniqueDirectory);
        if (!Directory.Exists(_techniqueDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The extracted technique directory does not exist: '{_techniqueDirectory}'.");
        }

        _techniqueDirectoryPrefix = _techniqueDirectory.EndsWith(
            Path.DirectorySeparatorChar)
            ? _techniqueDirectory
            : _techniqueDirectory + Path.DirectorySeparatorChar;
    }

    internal Iw3TechniqueSetSource ParseTechniqueSet(
        string techniqueSetPath,
        string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueSetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        string fullPath = Path.GetFullPath(techniqueSetPath);
        string name = assetName;

        var reader = CreateReader(fullPath, techniqueSet: true);
        var pendingSlots = new List<Iw3TechniqueSlot>();
        var techniqueNames = new Dictionary<Iw3TechniqueSlot, string>();

        while (reader.Peek().Kind != TokenKind.End)
        {
            if (reader.Peek().Kind == TokenKind.String &&
                reader.Peek(1).Kind == TokenKind.Colon)
            {
                Token displayName = reader.Read();
                reader.Expect(TokenKind.Colon, "Expected ':' after the technique-slot name.");
                if (!SlotsByDisplayName.TryGetValue(displayName.Text, out Iw3TechniqueSlot slot))
                {
                    throw reader.Error(
                        displayName,
                        $"Unknown IW3 technique slot '{displayName.Text}'.");
                }
                if (techniqueNames.ContainsKey(slot) || pendingSlots.Contains(slot))
                {
                    throw reader.Error(
                        displayName,
                        $"IW3 technique slot '{displayName.Text}' is assigned more than once.");
                }

                pendingSlots.Add(slot);
                continue;
            }

            if (pendingSlots.Count == 0)
            {
                throw reader.Error(
                    reader.Peek(),
                    "Expected a quoted IW3 technique-slot name.");
            }

            Token techniqueName = reader.ExpectOneOf(
                TokenKind.Identifier,
                TokenKind.String,
                "Expected a technique name after the technique-slot name.");
            if (string.IsNullOrWhiteSpace(techniqueName.Text))
                throw reader.Error(techniqueName, "A technique name cannot be empty.");
            reader.Expect(TokenKind.Semicolon, "Expected ';' after the technique name.");

            foreach (Iw3TechniqueSlot slot in pendingSlots)
                techniqueNames.Add(slot, techniqueName.Text);
            pendingSlots.Clear();
        }

        if (pendingSlots.Count != 0)
        {
            throw reader.Error(
                reader.Peek(),
                "The technique set ends before its pending slots receive a technique name.");
        }

        Iw3TechniqueSlotSource[] slots = SlotDefinitions
            .Select(definition =>
            {
                techniqueNames.TryGetValue(definition.Slot, out string? techniqueName);
                Iw3TechniqueSource? technique = techniqueName is null
                    ? null
                    : LoadTechnique(techniqueName);
                return new Iw3TechniqueSlotSource(
                    definition.Slot,
                    definition.DisplayName,
                    techniqueName,
                    technique);
            })
            .ToArray();
        if (slots.Length != TechniqueSlotCount)
            throw new InvalidOperationException("The IW3 technique-slot definition is incomplete.");

        return new Iw3TechniqueSetSource(name, Array.AsReadOnly(slots));
    }

    private Iw3TechniqueSource LoadTechnique(string techniqueName)
    {
        if (_techniqueCache.TryGetValue(techniqueName, out Iw3TechniqueSource? cached))
            return cached;

        string techniquePath = Path.GetFullPath(
            Path.Combine(_techniqueDirectory, techniqueName + ".tech"));
        if (!techniquePath.StartsWith(_techniqueDirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Technique name '{techniqueName}' resolves outside the extracted technique directory.");
        }
        if (!File.Exists(techniquePath))
        {
            throw new FileNotFoundException(
                $"The referenced extracted technique '{techniqueName}' was not found.",
                techniquePath);
        }

        var reader = CreateReader(techniquePath, techniqueSet: false);
        var passes = new List<Iw3TechniquePassSource>();
        while (reader.Peek().Kind != TokenKind.End)
            passes.Add(ParsePass(reader));
        if (passes.Count == 0)
            throw reader.Error(reader.Peek(), "An IW3 technique must contain at least one pass.");

        var technique = new Iw3TechniqueSource(
            techniqueName,
            Array.AsReadOnly(passes.ToArray()));
        _techniqueCache.Add(techniqueName, technique);
        return technique;
    }

    private static Iw3TechniquePassSource ParsePass(TokenReader reader)
    {
        reader.Expect(TokenKind.LeftBrace, "Expected '{' to begin a technique pass.");

        string? stateMapName = null;
        Iw3ShaderSource? vertexShader = null;
        Iw3ShaderSource? pixelShader = null;
        var routing = new List<Iw3VertexStreamRoutingSource>();

        while (reader.Peek().Kind != TokenKind.RightBrace)
        {
            Token keyword = reader.Expect(
                TokenKind.Identifier,
                "Expected a pass property, shader, vertex routing, or '}'.");
            switch (keyword.Text)
            {
                case "stateMap":
                    if (stateMapName is not null)
                        throw reader.Error(keyword, "A technique pass has more than one state map.");
                    stateMapName = reader.Expect(
                        TokenKind.String,
                        "Expected the quoted state-map name.").Text;
                    if (string.IsNullOrWhiteSpace(stateMapName))
                        throw reader.Error(keyword, "A state-map name cannot be empty.");
                    reader.Expect(TokenKind.Semicolon, "Expected ';' after the state-map name.");
                    break;

                case "vertexShader":
                    if (vertexShader is not null)
                        throw reader.Error(keyword, "A technique pass has more than one vertex shader.");
                    vertexShader = ParseShader(reader, Iw3ShaderStage.Vertex);
                    break;

                case "pixelShader":
                    if (pixelShader is not null)
                        throw reader.Error(keyword, "A technique pass has more than one pixel shader.");
                    pixelShader = ParseShader(reader, Iw3ShaderStage.Pixel);
                    break;

                case "vertex":
                    routing.Add(ParseVertexRouting(reader));
                    break;

                default:
                    throw reader.Error(keyword, $"Unknown technique-pass property '{keyword.Text}'.");
            }
        }

        reader.Expect(TokenKind.RightBrace, "Expected '}' to end the technique pass.");
        if (stateMapName is null)
            throw reader.Error(reader.Peek(), "The technique pass has no state map.");
        if (vertexShader is null)
            throw reader.Error(reader.Peek(), "The technique pass has no vertex shader.");
        if (pixelShader is null)
            throw reader.Error(reader.Peek(), "The technique pass has no pixel shader.");

        return new Iw3TechniquePassSource(
            stateMapName,
            vertexShader,
            pixelShader,
            Array.AsReadOnly(routing.ToArray()));
    }

    private static Iw3ShaderSource ParseShader(
        TokenReader reader,
        Iw3ShaderStage stage)
    {
        Token modelToken = reader.ExpectOneOf(
            TokenKind.Number,
            TokenKind.String,
            "Expected the shader-model version.");
        Iw3ShaderModel shaderModel = ParseShaderModel(reader, modelToken);
        Token programName = reader.Expect(
            TokenKind.String,
            "Expected the quoted shader program name.");
        if (string.IsNullOrWhiteSpace(programName.Text))
            throw reader.Error(programName, "A shader program name cannot be empty.");

        reader.Expect(TokenKind.LeftBrace, "Expected '{' to begin the shader arguments.");
        var arguments = new List<Iw3ShaderArgumentSource>();
        while (reader.Peek().Kind != TokenKind.RightBrace)
            arguments.Add(ParseShaderArgument(reader));
        reader.Expect(TokenKind.RightBrace, "Expected '}' to end the shader arguments.");

        return new Iw3ShaderSource(
            stage,
            shaderModel,
            programName.Text,
            Array.AsReadOnly(arguments.ToArray()));
    }

    private static Iw3ShaderModel ParseShaderModel(TokenReader reader, Token token)
    {
        string[] components = token.Text.Split('.', StringSplitOptions.None);
        if (components.Length != 2 ||
            !int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            throw reader.Error(token, $"Shader-model version '{token.Text}' is invalid.");
        }

        return new Iw3ShaderModel(major, minor);
    }

    private static Iw3ShaderArgumentSource ParseShaderArgument(TokenReader reader)
    {
        Iw3IndexedName destination = ParseIndexedName(reader, "shader argument destination");
        reader.Expect(TokenKind.Equal, "Expected '=' after the shader argument destination.");
        Iw3ShaderValueSource value = ParseShaderValue(reader);
        reader.Expect(TokenKind.Semicolon, "Expected ';' after the shader argument.");
        return new Iw3ShaderArgumentSource(destination, value);
    }

    private static Iw3ShaderValueSource ParseShaderValue(TokenReader reader)
    {
        Token sourceKind = reader.Expect(
            TokenKind.Identifier,
            "Expected a constant, sampler, float4, or material shader value.");
        switch (sourceKind.Text)
        {
            case "constant":
            case "sampler":
            {
                reader.Expect(TokenKind.Dot, "Expected '.' after the code-value kind.");
                var accessor = new StringBuilder();
                accessor.Append(reader.Expect(
                    TokenKind.Identifier,
                    "Expected a code-value accessor.").Text);
                while (reader.Peek().Kind == TokenKind.Dot)
                {
                    reader.Read();
                    accessor.Append('.');
                    accessor.Append(reader.Expect(
                        TokenKind.Identifier,
                        "Expected a code-value accessor component.").Text);
                }

                int? elementIndex = ParseOptionalIndex(reader);
                if (sourceKind.Text == "sampler" && elementIndex is not null)
                    throw reader.Error(sourceKind, "A code sampler cannot have an array index.");

                return new Iw3CodeShaderValueSource(
                    sourceKind.Text == "constant"
                        ? Iw3CodeShaderValueKind.Constant
                        : Iw3CodeShaderValueKind.Sampler,
                    accessor.ToString(),
                    elementIndex);
            }

            case "float4":
                reader.Expect(TokenKind.LeftParenthesis, "Expected '(' after 'float4'.");
                float x = ParseFloat(reader);
                reader.Expect(TokenKind.Comma, "Expected ',' in the float4 value.");
                float y = ParseFloat(reader);
                reader.Expect(TokenKind.Comma, "Expected ',' in the float4 value.");
                float z = ParseFloat(reader);
                reader.Expect(TokenKind.Comma, "Expected ',' in the float4 value.");
                float w = ParseFloat(reader);
                reader.Expect(TokenKind.RightParenthesis, "Expected ')' after the float4 value.");
                return new Iw3LiteralShaderValueSource(x, y, z, w);

            case "material":
                reader.Expect(TokenKind.Dot, "Expected '.' after 'material'.");
                if (reader.Peek().Kind == TokenKind.Hash)
                {
                    reader.Read();
                    Token hash = reader.Expect(
                        TokenKind.Number,
                        "Expected a numeric material-property hash.");
                    return new Iw3MaterialShaderValueSource(
                        null,
                        ParseUInt32(reader, hash, "material-property hash"));
                }

                Token property = reader.Expect(
                    TokenKind.Identifier,
                    "Expected a material-property name or hash.");
                return new Iw3MaterialShaderValueSource(property.Text, null);

            default:
                throw reader.Error(sourceKind, $"Unknown shader value kind '{sourceKind.Text}'.");
        }
    }

    private static Iw3VertexStreamRoutingSource ParseVertexRouting(TokenReader reader)
    {
        reader.Expect(TokenKind.Dot, "Expected '.' after 'vertex'.");
        Iw3IndexedName destination = ParseIndexedName(reader, "vertex-stream destination");
        reader.Expect(TokenKind.Equal, "Expected '=' after the vertex-stream destination.");
        Token code = reader.Expect(TokenKind.Identifier, "Expected 'code' in vertex routing.");
        if (code.Text != "code")
            throw reader.Error(code, "A vertex-stream source must begin with 'code'.");
        reader.Expect(TokenKind.Dot, "Expected '.' after 'code'.");
        Iw3IndexedName source = ParseIndexedName(reader, "vertex-stream source");
        reader.Expect(TokenKind.Semicolon, "Expected ';' after the vertex routing.");
        return new Iw3VertexStreamRoutingSource(destination, source);
    }

    private static Iw3IndexedName ParseIndexedName(TokenReader reader, string description)
    {
        string name = reader.Expect(
            TokenKind.Identifier,
            $"Expected the {description} name.").Text;
        return new Iw3IndexedName(name, ParseOptionalIndex(reader));
    }

    private static int? ParseOptionalIndex(TokenReader reader)
    {
        if (reader.Peek().Kind != TokenKind.LeftBracket)
            return null;

        reader.Read();
        Token index = reader.Expect(TokenKind.Number, "Expected a non-negative array index.");
        int parsedIndex = ParseNonNegativeInt32(reader, index, "array index");
        reader.Expect(TokenKind.RightBracket, "Expected ']' after the array index.");
        return parsedIndex;
    }

    private static int ParseNonNegativeInt32(
        TokenReader reader,
        Token token,
        string description)
    {
        if (!int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            throw reader.Error(token, $"The {description} '{token.Text}' is invalid.");
        return value;
    }

    private static uint ParseUInt32(
        TokenReader reader,
        Token token,
        string description)
    {
        string valueText = token.Text;
        NumberStyles styles = NumberStyles.None;
        if (valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            valueText = valueText[2..];
            styles = NumberStyles.AllowHexSpecifier;
        }

        if (!uint.TryParse(valueText, styles, CultureInfo.InvariantCulture, out uint value))
            throw reader.Error(token, $"The {description} '{token.Text}' is invalid.");
        return value;
    }

    private static float ParseFloat(TokenReader reader)
    {
        Token token = reader.Expect(TokenKind.Number, "Expected a float4 component.");
        if (!float.TryParse(
                token.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value) ||
            !float.IsFinite(value))
        {
            throw reader.Error(token, $"Float value '{token.Text}' is invalid.");
        }
        return value;
    }

    private static TokenReader CreateReader(string path, bool techniqueSet)
    {
        string text = File.ReadAllText(path);
        return new TokenReader(path, Lexer.Tokenize(path, text, techniqueSet));
    }

    private enum TokenKind
    {
        Identifier,
        String,
        Number,
        LeftBrace,
        RightBrace,
        LeftBracket,
        RightBracket,
        LeftParenthesis,
        RightParenthesis,
        Dot,
        Colon,
        Semicolon,
        Equal,
        Comma,
        Hash,
        End,
    }

    private readonly record struct Token(
        TokenKind Kind,
        string Text,
        int Line,
        int Column);

    private sealed class TokenReader(
        string sourcePath,
        IReadOnlyList<Token> tokens)
    {
        private int _index;

        internal Token Peek(int offset = 0)
        {
            int index = Math.Min(_index + offset, tokens.Count - 1);
            return tokens[index];
        }

        internal Token Read() => tokens[_index++];

        internal Token Expect(TokenKind kind, string message)
        {
            Token token = Peek();
            if (token.Kind != kind)
                throw Error(token, message);
            _index++;
            return token;
        }

        internal Token ExpectOneOf(
            TokenKind first,
            TokenKind second,
            string message)
        {
            Token token = Peek();
            if (token.Kind != first && token.Kind != second)
                throw Error(token, message);
            _index++;
            return token;
        }

        internal InvalidDataException Error(Token token, string message) =>
            new($"{sourcePath}({token.Line},{token.Column}): {message}");
    }

    private static class Lexer
    {
        internal static IReadOnlyList<Token> Tokenize(string sourcePath, string text, bool techniqueSet)
        {
            var tokens = new List<Token>();
            int index = 0;
            int line = 1;
            int column = 1;

            while (index < text.Length)
            {
                char current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    Advance(text, ref index, ref line, ref column);
                    continue;
                }
                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    while (index < text.Length && text[index] != '\n')
                        Advance(text, ref index, ref line, ref column);
                    continue;
                }

                int tokenLine = line;
                int tokenColumn = column;
                if (current == '"')
                {
                    tokens.Add(ReadString(
                        sourcePath,
                        text,
                        ref index,
                        ref line,
                        ref column));
                    continue;
                }
                // Techset statements name relative files (including 2d and sm2/
                // namespaces); technique bodies instead contain typed values.
                if (IsIdentifierStart(current) || (techniqueSet && char.IsAsciiDigit(current)))
                {
                    int start = index;
                    do
                    {
                        Advance(text, ref index, ref line, ref column);
                    }
                    while (index < text.Length &&
                           (IsIdentifierPart(text[index]) ||
                            (techniqueSet && text[index] == '/' &&
                             index + 1 < text.Length && text[index + 1] != '/')));
                    tokens.Add(new Token(
                        TokenKind.Identifier,
                        text[start..index],
                        tokenLine,
                        tokenColumn));
                    continue;
                }
                if (char.IsDigit(current) ||
                    ((current == '-' || current == '+') &&
                     index + 1 < text.Length &&
                     char.IsDigit(text[index + 1])))
                {
                    tokens.Add(ReadNumber(
                        sourcePath,
                        text,
                        ref index,
                        ref line,
                        ref column));
                    continue;
                }

                TokenKind? punctuation = current switch
                {
                    '{' => TokenKind.LeftBrace,
                    '}' => TokenKind.RightBrace,
                    '[' => TokenKind.LeftBracket,
                    ']' => TokenKind.RightBracket,
                    '(' => TokenKind.LeftParenthesis,
                    ')' => TokenKind.RightParenthesis,
                    '.' => TokenKind.Dot,
                    ':' => TokenKind.Colon,
                    ';' => TokenKind.Semicolon,
                    '=' => TokenKind.Equal,
                    ',' => TokenKind.Comma,
                    '#' => TokenKind.Hash,
                    _ => null,
                };
                if (punctuation is null)
                {
                    throw new InvalidDataException(
                        $"{sourcePath}({line},{column}): Unexpected character '{current}'.");
                }

                tokens.Add(new Token(
                    punctuation.Value,
                    current.ToString(),
                    tokenLine,
                    tokenColumn));
                Advance(text, ref index, ref line, ref column);
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, line, column));
            return Array.AsReadOnly(tokens.ToArray());
        }

        private static Token ReadString(
            string sourcePath,
            string text,
            ref int index,
            ref int line,
            ref int column)
        {
            int tokenLine = line;
            int tokenColumn = column;
            Advance(text, ref index, ref line, ref column);
            var value = new StringBuilder();

            while (index < text.Length)
            {
                char current = text[index];
                if (current == '"')
                {
                    Advance(text, ref index, ref line, ref column);
                    return new Token(
                        TokenKind.String,
                        value.ToString(),
                        tokenLine,
                        tokenColumn);
                }
                if (current == '\n' || current == '\r')
                {
                    throw new InvalidDataException(
                        $"{sourcePath}({tokenLine},{tokenColumn}): Unterminated string literal.");
                }
                if (current == '\\')
                {
                    Advance(text, ref index, ref line, ref column);
                    if (index >= text.Length)
                    {
                        throw new InvalidDataException(
                            $"{sourcePath}({tokenLine},{tokenColumn}): Unterminated string escape.");
                    }

                    char escaped = text[index] switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => throw new InvalidDataException(
                            $"{sourcePath}({line},{column}): Unsupported string escape '\\{text[index]}'."),
                    };
                    value.Append(escaped);
                    Advance(text, ref index, ref line, ref column);
                    continue;
                }

                value.Append(current);
                Advance(text, ref index, ref line, ref column);
            }

            throw new InvalidDataException(
                $"{sourcePath}({tokenLine},{tokenColumn}): Unterminated string literal.");
        }

        private static Token ReadNumber(
            string sourcePath,
            string text,
            ref int index,
            ref int line,
            ref int column)
        {
            int start = index;
            int tokenLine = line;
            int tokenColumn = column;
            if (text[index] is '-' or '+')
                Advance(text, ref index, ref line, ref column);

            if (index + 1 < text.Length &&
                text[index] == '0' &&
                text[index + 1] is 'x' or 'X')
            {
                Advance(text, ref index, ref line, ref column);
                Advance(text, ref index, ref line, ref column);
                int digitsStart = index;
                while (index < text.Length && Uri.IsHexDigit(text[index]))
                    Advance(text, ref index, ref line, ref column);
                if (index == digitsStart)
                {
                    throw new InvalidDataException(
                        $"{sourcePath}({tokenLine},{tokenColumn}): Hexadecimal value has no digits.");
                }
            }
            else
            {
                while (index < text.Length && char.IsDigit(text[index]))
                    Advance(text, ref index, ref line, ref column);
                if (index < text.Length && text[index] == '.')
                {
                    Advance(text, ref index, ref line, ref column);
                    while (index < text.Length && char.IsDigit(text[index]))
                        Advance(text, ref index, ref line, ref column);
                }
                if (index < text.Length && text[index] is 'e' or 'E')
                {
                    Advance(text, ref index, ref line, ref column);
                    if (index < text.Length && text[index] is '-' or '+')
                        Advance(text, ref index, ref line, ref column);
                    int exponentStart = index;
                    while (index < text.Length && char.IsDigit(text[index]))
                        Advance(text, ref index, ref line, ref column);
                    if (index == exponentStart)
                    {
                        throw new InvalidDataException(
                            $"{sourcePath}({tokenLine},{tokenColumn}): Numeric exponent has no digits.");
                    }
                }
            }

            return new Token(
                TokenKind.Number,
                text[start..index],
                tokenLine,
                tokenColumn);
        }

        private static bool IsIdentifierStart(char value) =>
            char.IsAsciiLetter(value) || value == '_';

        private static bool IsIdentifierPart(char value) =>
            char.IsAsciiLetterOrDigit(value) || value == '_';

        private static void Advance(
            string text,
            ref int index,
            ref int line,
            ref int column)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
            index++;
        }
    }
}
