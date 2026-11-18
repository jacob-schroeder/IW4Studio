using System.Globalization;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public static class ManagedCgCompiler
{
    public static byte[] Compile(string source, MaterialShaderKind stage)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Parser(source, stage).Compile();
    }

    private enum TokenKind { End, Identifier, Number, Symbol }

    private readonly record struct Token(TokenKind Kind, string Text, int Line, int Column)
    {
        public InvalidDataException Error(string message) => new($"source.cg({Line},{Column}): {message}");
    }

    private sealed class Lexer(string source)
    {
        private int _offset;
        private int _line = 1;
        private int _column = 1;
        private char Current => Peek(0);

        public Token Read()
        {
            SkipTrivia();
            int start = _offset;
            int line = _line, column = _column;
            if (start == source.Length)
                return new(TokenKind.End, "", line, column);

            TokenKind kind;
            if (char.IsAsciiLetter(Current) || Current == '_')
            {
                kind = TokenKind.Identifier;
                do Advance(); while (char.IsAsciiLetterOrDigit(Current) || Current == '_');
            }
            else if (char.IsAsciiDigit(Current) || Current == '.' && char.IsAsciiDigit(Peek(1)))
            {
                kind = TokenKind.Number;
                ReadDigits();
                if (Current == '.')
                {
                    Advance();
                    ReadDigits();
                }
                if (Current is 'e' or 'E')
                {
                    Advance();
                    if (Current is '+' or '-') Advance();
                    ReadDigits();
                }
                if (Current is 'h' or 'H' or 'f' or 'F') Advance();
            }
            else
            {
                kind = TokenKind.Symbol;
                char symbol = Current;
                Advance();
                if ((symbol is '+' or '-' or '*' or '<' or '>' or '=' or '!') && Current == '=' ||
                    (symbol is '+' or '-') && Current == symbol) Advance();
            }
            return new(kind, source[start.._offset], line, column);
        }

        private char Peek(int ahead) => _offset + ahead < source.Length ? source[_offset + ahead] : '\0';

        private void ReadDigits()
        {
            while (char.IsAsciiDigit(Current)) Advance();
        }

        private void SkipTrivia()
        {
            while (_offset < source.Length)
            {
                if (char.IsWhiteSpace(Current))
                {
                    Advance();
                    continue;
                }
                if (Current != '/') return;
                if (Peek(1) == '/')
                {
                    while (_offset < source.Length && Current is not ('\r' or '\n')) Advance();
                    continue;
                }
                if (Peek(1) != '*') return;

                var opening = new Token(TokenKind.Symbol, "/*", _line, _column);
                Advance();
                Advance();
                while (_offset < source.Length && (Current != '*' || Peek(1) != '/')) Advance();
                if (_offset == source.Length) throw opening.Error("Unterminated block comment.");
                Advance();
                Advance();
            }
        }

        private void Advance()
        {
            char current = source[_offset++];
            if (current is '\r' or '\n')
            {
                if (current != '\n' || _offset == 1 || source[_offset - 2] != '\r') _line++;
                _column = 1;
                return;
            }
            _column++;
        }
    }

    private readonly record struct CgType(int Width, bool Half = false, bool Boolean = false)
    {
        public int LaneMask => (1 << Width) - 1;
    }

    private readonly record struct Value(CgType Type, CgSource Source, int Defined);

    private sealed class Variable(Token name, CgType type, CgSource source, int defined)
    {
        public Token Name { get; } = name;
        public CgType Type { get; } = type;
        public CgSource Source { get; set; } = source;
        public int Defined { get; set; } = defined;
        public Value Read() => new(Type, Source, Defined);
    }

    private sealed class Parser
    {
        private const string Lanes = "xyzw";
        private const int MaxExpressionDepth = 128;
        private readonly Lexer _lexer;
        private readonly bool _vertex;
        private readonly List<CgParameter> _parameters = [];
        private readonly List<CgInstruction> _instructions = [];
        private readonly Dictionary<string, Variable> _variables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _samplers = new(StringComparer.Ordinal);
        private readonly HashSet<int> _samplerBindings = [];
        private readonly HashSet<int> _inputBindings = [];
        private readonly HashSet<int> _vertexUniformBindings = [];
        private readonly Dictionary<int, Variable> _outputs = [];
        private Token _current;
        private Token _next;
        private CgType _resultType;
        private int _returnBinding;
        private int _nextRegister;
        private int _expressionDepth;
        private CgSource? _predicate;

        public Parser(string source, MaterialShaderKind stage)
        {
            _vertex = stage switch
            {
                MaterialShaderKind.Vertex => true,
                MaterialShaderKind.Pixel => false,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
            _lexer = new Lexer(source);
            _current = _lexer.Read();
            _next = _lexer.Read();
        }

        public byte[] Compile()
        {
            while (Take("uniform")) ParseUniform();
            _resultType = ReadType(allowVoid: true, allowBoolean: false);
            Expect("main");
            Expect("(");
            if (!Take(")"))
            {
                do ParseParameter(); while (Take(","));
                Expect(")");
            }

            if (_resultType.Width != 0)
            {
                Expect(":");
                Token semantic = ReadName();
                _returnBinding = BindOutput(semantic, _resultType);
                if (_outputs.ContainsKey(_returnBinding))
                    throw semantic.Error($"Output {semantic.Text} is already bound.");
                _parameters.Add(new("main", semantic.Text.ToUpperInvariant(), _resultType.Width, _resultType.Half, IsOutput: true));
            }
            if (!_outputs.ContainsKey(0) && (_resultType.Width == 0 || _returnBinding != 0))
                throw _current.Error($"main must write {(_vertex ? "POSITION" : "COLOR0")}.");

            Expect("{");
            bool returned = false;
            while (_current.Text != "}")
            {
                if (returned) throw _current.Error("A statement after return is unreachable.");
                returned = ParseStatement();
            }
            Token end = Expect("}");
            if (_current.Kind != TokenKind.End) throw _current.Error("Expected the end of the shader after main.");
            if (!returned && _resultType.Width != 0) throw end.Error("main must return a value.");

            foreach ((int binding, Variable output) in _outputs)
                StoreOutput(binding, output.Type, output.Read(), output.Name);
            var usedInputs = new HashSet<int>();
            var usedSamplers = new HashSet<int>();
            var usedUniforms = new HashSet<int>();
            foreach (CgInstruction instruction in _instructions)
            {
                if (instruction.A.Kind == CgRegisterKind.Input) usedInputs.Add(instruction.A.Index);
                if (instruction.B is { Kind: CgRegisterKind.Input } b) usedInputs.Add(b.Index);
                if (instruction.Operation == CgOp.Texture2D) usedSamplers.Add(instruction.Sampler);
                if (instruction.A.Kind == CgRegisterKind.Uniform) usedUniforms.Add(instruction.A.Index);
                if (instruction.B is { Kind: CgRegisterKind.Uniform } uniform) usedUniforms.Add(uniform.Index);
            }
            for (int i = 0; i < _parameters.Count; i++)
            {
                CgParameter parameter = _parameters[i];
                HashSet<int> bindings = parameter.IsUniform ? usedUniforms : parameter.Components == 0 ? usedSamplers : usedInputs;
                bool referenced = parameter.IsOutput || bindings.Contains(parameter.Binding);
                _parameters[i] = parameter with { Referenced = referenced };
            }
            return _vertex
                ? ManagedCgVertexEmitter.Emit(_parameters, _instructions)
                : ManagedCgFragmentEmitter.Emit(_parameters, _instructions);
        }

        private void ParseUniform()
        {
            Token start = _current;
            if (Take("sampler2D"))
            {
                if (_vertex) throw start.Error("Managed vertex shaders do not support texture sampling.");
                ParseSampler();
                return;
            }

            CgType type = ReadType(allowBoolean: false);
            Token name = ReadName();
            int binding = _parameters.Count;
            string semantic = "";
            if (_vertex)
            {
                Expect(":");
                Token register = ReadName();
                if (!TryIndex(register.Text, "C", RsxVertexConstantLayout.MaximumDestination, out binding))
                    throw register.Error($"Expected a vertex uniform binding from C0 through C{RsxVertexConstantLayout.MaximumDestination}.");
                if (!_vertexUniformBindings.Add(binding))
                    throw register.Error($"Vertex uniform binding C{binding} is already declared.");
                semantic = $"C{binding}";
            }
            else if (_current.Text == ":")
            {
                throw _current.Error("Managed pixel numeric uniforms bind by name.");
            }
            Expect(";");
            // Fill unused hardware selectors with a declared lane for narrow uniforms.
            string selectors = Lanes[..type.Width].PadRight(4, 'x');
            Declare(name, type, new(CgRegisterKind.Uniform, binding, type.Half, selectors), type.LaneMask);
            _parameters.Add(new(name.Text, semantic, type.Width, type.Half, Binding: binding, IsUniform: true));
        }

        private void ParseSampler()
        {
            Token name = ReadName();
            Expect(":");
            Token semantic = ReadName();
            if (!TryIndex(semantic.Text, "TEXUNIT", 15, out int unit))
                throw semantic.Error("Expected a sampler binding from TEXUNIT0 through TEXUNIT15.");
            if (_variables.ContainsKey(name.Text) || !_samplers.TryAdd(name.Text, unit))
                throw name.Error($"'{name.Text}' is already declared.");
            if (!_samplerBindings.Add(unit)) throw semantic.Error($"Sampler binding {semantic.Text} is already declared.");
            _parameters.Add(new(name.Text, $"TEXUNIT{unit}", 0, Binding: unit));
            Expect(";");
        }

        private void ParseParameter()
        {
            bool output = Take("out");
            if (!output) Take("in");
            CgType type = ReadType(allowBoolean: false);
            Token name = ReadName();
            Expect(":");
            Token semantic = ReadName();
            string bindingName = semantic.Text.ToUpperInvariant();
            int binding;
            if (output)
            {
                binding = BindOutput(semantic, type);
                var variable = Declare(name, type, Temporary(type));
                if (!_outputs.TryAdd(binding, variable)) throw semantic.Error($"Output {semantic.Text} is already bound.");
            }
            else
            {
                binding = BindInput(semantic);
                if (!_inputBindings.Add(binding)) throw semantic.Error($"Input {semantic.Text} is already bound.");
                Declare(name, type, new(CgRegisterKind.Input, binding), type.LaneMask);
            }
            _parameters.Add(new(name.Text, bindingName, type.Width, type.Half, output, binding));
        }

        private int BindInput(Token semantic)
        {
            string name = semantic.Text.ToUpperInvariant();
            if (_vertex)
            {
                if (TryIndex(name, "ATTR", 15, out int attribute)) return attribute;
                throw semantic.Error("Expected a vertex input binding from ATTR0 through ATTR15.");
            }

            if (TryIndex(name, "TEXCOORD", 7, out int coordinate)) return 4 + coordinate;
            if (name == "COLOR0") return 1;
            if (name == "COLOR1") return 2;
            throw semantic.Error("Expected a pixel input binding of COLOR0, COLOR1, or TEXCOORD0 through TEXCOORD7.");
        }

        private int BindOutput(Token semantic, CgType type)
        {
            if (type.Width != 4) throw semantic.Error("Managed shader outputs must have four components.");
            string name = semantic.Text.ToUpperInvariant();
            if (!_vertex)
            {
                if (name == "COLOR0") return 0;
                throw semantic.Error("Managed pixel shaders support only the COLOR0 output.");
            }
            if (name == "POSITION") return 0;
            if (name == "COLOR0") return 1;
            if (TryIndex(name, "TEXCOORD", 7, out int coordinate)) return 7 + coordinate;
            throw semantic.Error("Expected a vertex output binding of POSITION, COLOR0, or TEXCOORD0 through TEXCOORD7.");
        }

        private static bool TryIndex(string name, string prefix, int maximum, out int index)
        {
            index = 0;
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            ReadOnlySpan<char> suffix = name.AsSpan(prefix.Length);
            return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                index <= maximum && (suffix.Length == 1 || suffix[0] != '0');
        }

        private bool ParseStatement()
        {
            Token start = _current;
            if (Take("return"))
            {
                if (_resultType.Width != 0) StoreOutput(_returnBinding, _resultType, ParseExpression(), start);
                Expect(";");
                return true;
            }

            if (Take("if"))
            {
                ParseConditional(start);
                return false;
            }

            if (TryType(_current.Text, out _))
            {
                CgType type = ReadType();
                do
                {
                    Token name = ReadName();
                    var variable = Declare(name, type, Temporary(type));
                    if (Take("=")) Assign(variable, Lanes[..type.Width], ParseExpression(), name);
                } while (Take(","));
                Expect(";");
                return false;
            }

            ParseAssignment();
            return false;
        }

        private void ParseConditional(Token at)
        {
            if (_vertex) throw at.Error("Managed conditional assignments require a pixel shader.");
            Expect("(");
            Value condition = ParseExpression();
            Expect(")");
            if (!condition.Type.Boolean) throw at.Error("An if condition must be a scalar bool.");
            RequireDefined(condition, at);

            // A write in the body may change the variable that supplied the condition.
            CgSource predicate = Temporary(condition.Type);
            Write(predicate, "x", condition);
            _predicate = predicate with { Swizzle = "xxxx" };
            var definedBefore = new Dictionary<Variable, int>();
            if (Take("{"))
            {
                while (!Take("}")) ParseAssignment(definedBefore);
            }
            else
            {
                ParseAssignment(definedBefore);
            }
            _predicate = null;
            // Every component must also have a value on the path that skips the body.
            foreach ((Variable variable, int defined) in definedBefore)
                variable.Defined = defined;
        }

        private void ParseAssignment(Dictionary<Variable, int>? definedBefore = null)
        {
            Token target = ReadName();
            Variable existing = FindVariable(target);
            if (existing.Source.Kind == CgRegisterKind.Uniform)
                throw target.Error($"Uniform '{target.Text}' is read-only.");
            string mask = Take(".") ? ReadSwizzle(existing.Type) : Lanes[..existing.Type.Width];
            if (mask.Distinct().Count() != mask.Length)
                throw target.Error("A write swizzle cannot select the same component twice.");
            Token operation = Consume();
            if (operation.Text is not ("=" or "+=" or "-=" or "*="))
                throw operation.Error("Expected an assignment (=, +=, -=, or *=).");
            Value right = ParseExpression();
            if (operation.Text != "=")
            {
                CgOp op = operation.Text switch { "+=" => CgOp.Add, "-=" => CgOp.Subtract, _ => CgOp.Multiply };
                right = Binary(op, Select(Read(existing), mask), right, operation);
            }
            definedBefore?.TryAdd(existing, existing.Defined);
            Assign(existing, mask, right, operation);
            Expect(";");
        }

        private void Assign(Variable target, string lanes, Value input, Token at)
        {
            if (target.Type.Boolean != input.Type.Boolean)
                throw at.Error("Use an explicit conversion between bool and numeric values.");
            Value value = Shape(input, lanes.Length, at);
            RequireDefined(value, at);
            if (target.Source.Kind == CgRegisterKind.Input)
            {
                CgSource local = Temporary(target.Type);
                if (_predicate is not null || lanes.Length != target.Type.Width)
                    Write(local, Lanes[..target.Type.Width], target.Read());
                target.Source = local;
            }
            Write(target.Source, lanes, value, _predicate);
            foreach (char lane in lanes) target.Defined |= 1 << Lanes.IndexOf(lane);
        }

        private void StoreOutput(int binding, CgType type, Value value, Token at)
        {
            RequireNumeric(value, at);
            value = Shape(value, type.Width, at);
            RequireDefined(value, at);
            Write(new(CgRegisterKind.Output, binding, type.Half), Lanes[..type.Width], value);
        }

        private Value Read(Variable variable)
        {
            if (variable.Type.Half && variable.Source.Kind == CgRegisterKind.Input)
            {
                // COLOR interpolants ignore the RSX source-precision bits. Narrow half inputs in a MOV.
                CgSource local = Temporary(variable.Type);
                Write(local, Lanes[..variable.Type.Width], variable.Read());
                variable.Source = local;
            }
            return variable.Read();
        }

        private Value ParseExpression()
        {
            Value left = ParseRelational();
            while (_current.Text is "==" or "!=")
            {
                Token op = Consume();
                left = Compare(op.Text == "==" ? CgOp.Equal : CgOp.NotEqual, left, ParseRelational(), op);
            }
            return left;
        }

        private Value ParseRelational()
        {
            Value left = ParseSum();
            while (_current.Text is "<" or "<=" or ">" or ">=")
            {
                Token op = Consume();
                CgOp operation = op.Text switch
                {
                    "<" => CgOp.LessThan,
                    "<=" => CgOp.LessThanOrEqual,
                    ">" => CgOp.GreaterThan,
                    _ => CgOp.GreaterThanOrEqual
                };
                left = Compare(operation, left, ParseSum(), op);
            }
            return left;
        }

        private Value ParseSum()
        {
            Value left = ParseProduct();
            while (_current.Text is "+" or "-")
            {
                Token op = Consume();
                left = Binary(op.Text == "+" ? CgOp.Add : CgOp.Subtract, left, ParseProduct(), op);
            }
            return left;
        }

        private Value ParseProduct()
        {
            Value left = ParseUnary();
            while (_current.Text == "*")
            {
                Token op = Consume();
                left = Binary(CgOp.Multiply, left, ParseUnary(), op);
            }
            return left;
        }

        private Value ParseUnary()
        {
            if (++_expressionDepth > MaxExpressionDepth)
                throw _current.Error("Expression nesting exceeds the managed compiler limit.");
            try
            {
                if (_current.Text is "+" or "-")
                {
                    Token sign = Consume();
                    Value value = ParseUnary();
                    RequireNumeric(value, sign);
                    return sign.Text == "-" ? value with { Source = value.Source with { Negate = !value.Source.Negate } } : value;
                }
                Token start = _current;
                Value result;
                if (Take("("))
                {
                    if (_next.Text == ")" && TryType(_current.Text, out _))
                    {
                        CgType cast = ReadType();
                        Expect(")");
                        return Cast(ParseUnary(), cast, start, truncate: true);
                    }
                    result = ParseExpression();
                    Expect(")");
                }
                else if (start.Kind == TokenKind.Number)
                {
                    Consume();
                    result = Number(start);
                }
                else if (start.Text is "true" or "false")
                {
                    Consume();
                    result = Literal(start.Text == "true" ? 1 : 0, new(1, Boolean: true));
                }
                else if (TryType(start.Text, out _))
                {
                    CgType type = ReadType();
                    Expect("(");
                    var arguments = new List<Value>();
                    do arguments.Add(ParseExpression()); while (Take(","));
                    Expect(")");
                    result = Construct(type, arguments, start);
                }
                else if (Take("tex2D"))
                {
                    result = Texture(start);
                }
                else if (_next.Text == "(" && start.Text is ("dot" or "min" or "max" or "saturate"))
                {
                    Consume();
                    result = Intrinsic(start);
                }
                else
                {
                    Token name = ReadName();
                    if (_current.Text == "(") throw name.Error($"Function '{name.Text}' is outside the managed Cg subset.");
                    result = Read(FindVariable(name));
                }
                while (Take(".")) result = Select(result, ReadSwizzle(result.Type));
                return result;
            }
            finally
            {
                _expressionDepth--;
            }
        }

        private Value Number(Token token)
        {
            string text = token.Text;
            bool half = text[^1] is 'h' or 'H';
            if (half && _vertex) throw token.Error("Managed vertex shaders support float precision.");
            if (half || text[^1] is 'f' or 'F') text = text[..^1];
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                !float.IsFinite(number))
                throw token.Error($"Invalid finite number '{token.Text}'.");
            if (half)
            {
                number = (float)(Half)number;
                if (!float.IsFinite(number)) throw token.Error($"Half literal '{token.Text}' is out of range.");
            }
            return Literal(number, new(1, half));
        }

        private static Value Literal(float number, CgType type) =>
            new(type, new(CgRegisterKind.Constant, Swizzle: "xxxx", Literal: [number, 0, 0, 0]), 1);

        private Value Texture(Token operation)
        {
            if (_vertex) throw operation.Error("Managed vertex shaders do not support texture sampling.");
            if (_predicate is not null)
                throw operation.Error("Sample textures before using their values in conditional assignments.");
            Expect("(");
            Token sampler = ReadName();
            if (!_samplers.TryGetValue(sampler.Text, out int unit))
                throw sampler.Error($"'{sampler.Text}' is not a declared sampler2D.");
            Expect(",");
            Value coordinates = ParseExpression();
            Expect(")");
            RequireNumeric(coordinates, operation);
            if (coordinates.Type.Width != 2) throw operation.Error("tex2D coordinates must have two components.");
            RequireDefined(coordinates, operation);
            var type = new CgType(4);
            CgSource target = Temporary(type);
            _instructions.Add(new(CgOp.Texture2D, Destination(target, Lanes), coordinates.Source, Sampler: unit));
            return new(type, target, type.LaneMask);
        }

        private Value Intrinsic(Token at)
        {
            Expect("(");
            Value left = ParseExpression();
            if (at.Text == "saturate")
            {
                Expect(")");
                RequireNumeric(left, at);
                RequireDefined(left, at);
                CgType type = left.Type;
                CgSource target = Temporary(type);
                _instructions.Add(new(CgOp.Saturate, Destination(target, Lanes[..type.Width]), left.Source));
                return new(type, target, type.LaneMask);
            }

            Expect(",");
            Value right = ParseExpression();
            Expect(")");
            if (at.Text == "min") return Binary(CgOp.Minimum, left, right, at);
            if (at.Text == "max") return Binary(CgOp.Maximum, left, right, at);

            RequireNumeric(left, at);
            RequireNumeric(right, at);
            if (left.Type.Width != right.Type.Width || left.Type.Width < 2)
                throw at.Error("dot requires two vectors of the same width, with two to four components.");
            CgOp operation = left.Type.Width switch { 2 => CgOp.Dot2, 3 => CgOp.Dot3, _ => CgOp.Dot4 };
            return EmitBinary(operation, left, right, new(1, left.Type.Half && right.Type.Half), at);
        }

        private Value Construct(CgType type, List<Value> arguments, Token at)
        {
            if (arguments.Count == 1) return Cast(arguments[0], type, at);
            if (type.Boolean) throw at.Error("bool requires one scalar argument.");
            if (arguments.Sum(value => value.Type.Width) != type.Width)
                throw at.Error($"This constructor requires {type.Width} components.");

            foreach (Value value in arguments)
            {
                RequireNumeric(value, at);
                RequireDefined(value, at);
            }
            if (arguments.All(value => value.Source.Literal is not null))
            {
                var literal = new float[4];
                int lane = 0;
                foreach (Value value in arguments)
                {
                    if (value.Source.Literal is not { } words) throw new InvalidOperationException();
                    for (int i = 0; i < value.Type.Width; i++)
                    {
                        float number = words[Lanes.IndexOf(value.Source.Swizzle[i])];
                        if (value.Source.Negate) number = -number;
                        if (type.Half) number = (float)(Half)number;
                        if (!float.IsFinite(number)) throw at.Error("A half constructor component is out of range.");
                        literal[lane++] = number;
                    }
                }
                return new(type, new(CgRegisterKind.Constant, Literal: literal), type.LaneMask);
            }

            CgSource target = Temporary(type);
            int offset = 0;
            foreach (Value value in arguments)
            {
                Write(target, Lanes.AsSpan(offset, value.Type.Width).ToString(), value);
                offset += value.Type.Width;
            }
            return new(type, target, type.LaneMask);
        }

        private Value Cast(Value value, CgType type, Token at, bool truncate = false)
        {
            if (type.Boolean)
            {
                if (value.Type.Width != 1) throw at.Error("bool requires a scalar argument.");
                return value.Type.Boolean ? value : Compare(CgOp.NotEqual, value, Literal(0, new(1)), at);
            }
            if (value.Type.Boolean) value = value with { Type = new(1) };
            if (truncate && value.Type.Width > type.Width) value = Select(value, Lanes[..type.Width]);
            return Convert(Shape(value, type.Width, at), type);
        }

        private Value Convert(Value value, CgType type)
        {
            if (!type.Half || value.Type.Half || value.Defined == 0)
                return value with { Type = type };
            CgSource target = Temporary(type);
            var written = new string(Lanes.AsSpan(0, type.Width).ToArray()
                .Where(lane => (value.Defined & (1 << Lanes.IndexOf(lane))) != 0).ToArray());
            Write(target, written, Select(value, written));
            return new(type, target, value.Defined);
        }

        private Value Binary(CgOp operation, Value left, Value right, Token at)
        {
            RequireNumeric(left, at);
            RequireNumeric(right, at);
            int width = Math.Max(left.Type.Width, right.Type.Width);
            left = Shape(left, width, at);
            right = Shape(right, width, at);
            return EmitBinary(operation, left, right, new(width, left.Type.Half && right.Type.Half), at);
        }

        private Value Compare(CgOp operation, Value left, Value right, Token at)
        {
            if (left.Type.Width != 1 || right.Type.Width != 1 || left.Type.Boolean != right.Type.Boolean)
                throw at.Error("Comparisons require two numeric scalars or two bool values.");
            if (left.Type.Boolean && operation is not (CgOp.Equal or CgOp.NotEqual))
                throw at.Error("Only == and != compare bool values.");
            return EmitBinary(operation, left, right, new(1, Boolean: true), at);
        }

        private Value EmitBinary(CgOp operation, Value left, Value right, CgType type, Token at)
        {
            RequireDefined(left, at);
            RequireDefined(right, at);
            CgSource target = Temporary(type);
            _instructions.Add(new(operation, Destination(target, Lanes[..type.Width]), left.Source, right.Source));
            return new(type, target, type.LaneMask);
        }

        private static void RequireNumeric(Value value, Token at)
        {
            if (value.Type.Boolean) throw at.Error("A numeric value is required.");
        }

        private static void RequireDefined(Value value, Token at)
        {
            if (value.Defined != value.Type.LaneMask)
                throw at.Error("This expression reads an uninitialized component.");
        }

        private static Value Shape(Value value, int width, Token at)
        {
            if (value.Type.Width == width) return value;
            if (value.Type.Width == 1) return Select(value, new string('x', width));
            throw at.Error($"Cannot use a {value.Type.Width}-component value as a {width}-component value.");
        }

        private static Value Select(Value value, string lanes)
        {
            int defined = 0;
            Span<char> swizzle = stackalloc char[4];
            for (int i = 0; i < lanes.Length; i++)
            {
                int lane = Lanes.IndexOf(lanes[i]);
                if ((value.Defined & (1 << lane)) != 0) defined |= 1 << i;
                swizzle[i] = value.Source.Swizzle[lane];
            }
            swizzle[lanes.Length..].Fill(swizzle[0]);
            return new(value.Type with { Width = lanes.Length }, value.Source with { Swizzle = new string(swizzle) }, defined);
        }

        private string ReadSwizzle(CgType type)
        {
            Token name = ReadName();
            if (type.Boolean) throw name.Error("A bool has no swizzle components.");
            if (name.Text.Length > 4) throw name.Error("A swizzle can have up to four components.");
            string alphabet = "rgba".Contains(name.Text[0]) ? "rgba" : Lanes;
            Span<char> result = stackalloc char[name.Text.Length];
            for (int i = 0; i < name.Text.Length; i++)
            {
                int lane = alphabet.IndexOf(name.Text[i]);
                if (lane < 0 || lane >= type.Width)
                    throw name.Error($"Invalid swizzle '{name.Text}' for a {type.Width}-component value.");
                result[i] = Lanes[lane];
            }
            return new string(result);
        }

        private static CgDestination Destination(CgSource storage, string lanes) => new(storage.Kind, storage.Index, storage.Half,
            new string(Lanes.Where(lanes.Contains).ToArray()));

        private void Write(CgSource storage, string lanes, Value value, CgSource? predicate = null)
        {
            Span<char> aligned = stackalloc char[4];
            aligned.Fill(value.Source.Swizzle[0]);
            for (int i = 0; i < lanes.Length; i++)
                aligned[Lanes.IndexOf(lanes[i])] = value.Source.Swizzle[i];
            _instructions.Add(new(CgOp.Move, Destination(storage, lanes),
                value.Source with { Swizzle = new string(aligned) }, Predicate: predicate));
        }

        private CgSource Temporary(CgType type) => new(CgRegisterKind.Temporary, _nextRegister++, type.Half);

        private Variable Declare(Token name, CgType type, CgSource storage, int defined = 0)
        {
            var variable = new Variable(name, type, storage, defined);
            if (_samplers.ContainsKey(name.Text) || !_variables.TryAdd(name.Text, variable))
                throw name.Error($"'{name.Text}' is already declared.");
            return variable;
        }

        private Variable FindVariable(Token name) => _variables.TryGetValue(name.Text, out var variable)
            ? variable : throw name.Error($"Unknown variable '{name.Text}'.");

        private CgType ReadType(bool allowVoid = false, bool allowBoolean = true)
        {
            Token token = Consume();
            if (allowVoid && token.Text == "void") return default;
            if (!TryType(token.Text, out var type))
                throw token.Error($"Unsupported Cg type '{token.Text}'. Expected a numeric scalar or vector or a scalar bool.");
            if (type.Boolean && !allowBoolean)
                throw token.Error("Shader parameters and return values require a numeric type.");
            if (_vertex && type.Half) throw token.Error("Managed vertex shaders support float precision.");
            return type;
        }

        private static bool TryType(string text, out CgType type)
        {
            if (text == "bool")
            {
                type = new(1, Boolean: true);
                return true;
            }
            type = default;
            bool half = text.StartsWith("half", StringComparison.Ordinal);
            if (!half && !text.StartsWith("float", StringComparison.Ordinal)) return false;
            int prefix = half ? 4 : 5;
            if (text.Length == prefix)
            {
                type = new(1, half);
                return true;
            }
            if (text.Length != prefix + 1 || text[^1] is < '1' or > '4') return false;
            type = new(text[^1] - '0', half);
            return true;
        }

        private Token ReadName()
        {
            Token token = Consume();
            if (token.Kind != TokenKind.Identifier ||
                token.Text is "uniform" or "sampler2D" or "in" or "out" or "inout" or "void" or "return" or "main" or "tex2D" ||
                token.Text is "if" or "else" or "for" or "while" or "do" or "switch" or "case" or "default" or "break" or
                    "continue" or "discard" or "struct" or "typedef" or "const" or "static" or "true" or "false" ||
                TryType(token.Text, out _))
                throw token.Error("Expected an identifier.");
            return token;
        }

        private Token Consume()
        {
            Token token = _current;
            _current = _next;
            _next = _lexer.Read();
            return token;
        }

        private bool Take(string text)
        {
            if (_current.Text != text) return false;
            Consume();
            return true;
        }

        private Token Expect(string text)
        {
            if (_current.Text != text) throw _current.Error($"Expected '{text}'.");
            return Consume();
        }
    }
}
