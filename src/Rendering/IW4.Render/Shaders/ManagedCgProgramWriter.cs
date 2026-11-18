using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace IW4.Render.Shaders;

/// <summary>Writes the big-endian revision 6 Cg container consumed by IW4 on PS3.</summary>
internal static class ManagedCgProgramWriter
{
    private const int HeaderSize = 0x20;
    private const int ParameterSize = 0x30;
    private const uint VertexConstantResource = 0x882;
    private const uint FragmentConstantResource = 0xcb8;

    public static byte[] Write(bool vertex, IReadOnlyList<CgParameter> parameters,
        ReadOnlySpan<byte> descriptor, ReadOnlySpan<byte> microcode,
        IReadOnlyList<(int Register, float[] Value)>? vertexDefaults = null,
        IReadOnlyDictionary<int, List<int>>? fragmentPatches = null)
    {
        int count = checked(parameters.Count + (vertexDefaults?.Count ?? 0));
        using var stream = new MemoryStream();
        stream.SetLength(checked(HeaderSize + count * ParameterSize));
        stream.Position = stream.Length;

        uint argument = 0;
        for (int i = 0; i < parameters.Count; i++)
        {
            CgParameter parameter = parameters[i];
            bool sampler = parameter.Components == 0;
            bool uniform = sampler || parameter.IsUniform;
            int name = WriteString(parameter.Name);
            int semantic = parameter.Semantic.Length == 0 ? 0 : WriteString(parameter.Semantic);
            uint type = sampler ? 0x42au : (parameter.Half ? 0x401u : 0x415u) + (uint)(parameter.Components - 1);
            uint resource = Resource(vertex, parameter);
            uint register = vertex && parameter.IsUniform ? checked((uint)parameter.Binding) : uint.MaxValue;
            int patch = 0;
            bool referenced = parameter.Referenced;
            if (!vertex && parameter.IsUniform)
            {
                if (fragmentPatches is not null && fragmentPatches.TryGetValue(parameter.Binding, out List<int>? sites) && sites.Count != 0)
                    patch = WritePatchList(sites);
                referenced = patch != 0;
            }

            WriteRow(i, type, resource, uniform ? 0x1006u : 0x1005u, register,
                name, 0, patch, semantic, parameter.IsOutput ? 0x1002u : 0x1001u,
                uniform ? uint.MaxValue : argument++, referenced, vertex && parameter.IsUniform ? 1u : 0u);
        }
        if (vertexDefaults is not null)
        {
            Span<byte> wordBuffer = stackalloc byte[4];
            for (int i = 0; i < vertexDefaults.Count; i++)
            {
                var (register, vector) = vertexDefaults[i];
                int name = WriteString($"internal-constant-{i}");
                int value = Align(4);
                foreach (float component in vector)
                {
                    BinaryPrimitives.WriteSingleBigEndian(wordBuffer, component);
                    stream.Write(wordBuffer);
                }
                WriteRow(parameters.Count + i, 0x418, VertexConstantResource, 0x1007, (uint)register,
                    name, value, 0, 0, 0x1001, uint.MaxValue - 1, true, 0);
            }
        }

        int descriptorOffset = Align(16);
        stream.Write(descriptor);
        int uploadOffset = Align(16);
        stream.Write(microcode);
        byte[] program = stream.ToArray();
        Word(program, 0, vertex ? 7003u : 7004u);
        Word(program, 4, 6);
        Word(program, 8, (uint)program.Length);
        Word(program, 12, (uint)count);
        Word(program, 16, HeaderSize);
        Word(program, 20, (uint)descriptorOffset);
        Word(program, 24, (uint)microcode.Length);
        Word(program, 28, (uint)uploadOffset);
        return program;

        int WriteString(string text)
        {
            int offset = checked((int)stream.Position);
            stream.Write(Encoding.ASCII.GetBytes(text));
            stream.WriteByte(0);
            return offset;
        }

        int Align(int alignment)
        {
            while (stream.Position % alignment != 0) stream.WriteByte(0);
            return checked((int)stream.Position);
        }

        int WritePatchList(List<int> sites)
        {
            int offset = Align(4);
            Span<byte> word = stackalloc byte[4];
            Word(word, 0, (uint)sites.Count);
            stream.Write(word);
            foreach (int site in sites)
            {
                Word(word, 0, (uint)site);
                stream.Write(word);
            }
            return offset;
        }

        void WriteRow(int index, uint type, uint resource, uint variability, uint register,
            int name, int value, int patch, int semantic, uint direction, uint number, bool referenced, uint flags)
        {
            Span<byte> row = stream.GetBuffer().AsSpan(HeaderSize + index * ParameterSize, ParameterSize);
            Word(row, 0, type);
            Word(row, 4, resource);
            Word(row, 8, variability);
            Word(row, 12, register);
            Word(row, 16, (uint)name);
            Word(row, 20, (uint)value);
            Word(row, 24, (uint)patch);
            Word(row, 28, (uint)semantic);
            Word(row, 32, direction);
            Word(row, 36, number);
            Word(row, 40, referenced ? 1u : 0u);
            Word(row, 44, flags);
        }
    }

    private static uint Resource(bool vertex, CgParameter parameter)
    {
        if (parameter.IsUniform) return vertex ? VertexConstantResource : FragmentConstantResource;
        string semantic = parameter.Semantic;
        if (semantic == "POSITION" && vertex) return 0x8c3;
        if (semantic.StartsWith("TEXCOORD", StringComparison.Ordinal)) return 0xc94 + Index(8);
        if (semantic.StartsWith("TEXUNIT", StringComparison.Ordinal)) return 0x800 + Index(7);
        if (semantic.StartsWith("COLOR", StringComparison.Ordinal)) return (vertex ? 0x8c5u : 0xac5u) + Index(5);
        if (semantic.StartsWith("ATTR", StringComparison.Ordinal) && vertex) return 0x841 + Index(4);
        throw new InvalidDataException($"Unsupported Cg parameter semantic '{semantic}'.");

        uint Index(int start) => uint.Parse(semantic.AsSpan(start), CultureInfo.InvariantCulture);
    }

    private static void Word(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes[offset..], value);
}
