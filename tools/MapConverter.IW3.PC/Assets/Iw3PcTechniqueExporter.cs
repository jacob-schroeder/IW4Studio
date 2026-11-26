using MapConverter.IW3.PC.Assets.TechniqueSet;
using System.Buffers.Binary;
using System.Text;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

// Writes selected technique graphs using their D3D9 CTAB destinations and
// serialized IW3 argument and vertex-routing tables.
internal sealed class Iw3PcTechniqueExporter(Iw3PcZone zone)
{
    private readonly Dictionary<int, string> techniques = [];
    private readonly Dictionary<string, (int Offset, int Length)> programs = new(StringComparer.Ordinal);

    internal void Export(Iw3PcAsset asset, Action<string, byte[]> write)
    {
        var setText = new StringBuilder();
        foreach (var (techniqueSlot, displayName) in Iw3TechniqueSlots.Definitions)
        {
            int slot = (int)techniqueSlot;
            int technique = zone.Pointer(asset.Offset + 12 + slot * 4);
            if (technique == 0) continue;
            if (!techniques.TryGetValue(technique, out string? qualified))
            {
                qualified = asset.Name + "/slot" + slot + "/" + zone.String(technique);
                techniques.Add(technique, qualified);
                write("techniques/" + qualified + ".tech", ReadTechnique(technique, write));
            }
            if (setText.Length != 0) setText.Append('\n');
            setText.Append('"').Append(displayName).Append("\":\n  ")
                .Append(qualified).Append(";\n");
        }
        write("techsets/" + asset.Name + ".techset", Encoding.UTF8.GetBytes(setText.ToString()));
    }

    private byte[] ReadTechnique(int technique, Action<string, byte[]> write)
    {
        var text = new StringBuilder();
        int count = U16(zone, technique + 6);
        if (count == 0) throw new InvalidDataException($"IW3 technique '{zone.String(technique)}' has no passes.");
        for (int index = 0; index < count; index++)
        {
            int pass = technique + 8 + index * 20;
            string vertex = ReadShader(zone.Pointer(pass + 4), true, write);
            string pixel = ReadShader(zone.Pointer(pass + 8), false, write);
            text.Append("{\n  stateMap \"passthrough\";\n  vertexShader ")
                .Append(vertex).Append("\n  {\n")
                .Append(Iw3PcShaderArgumentReader.ReadArguments(zone, pass, zone.Pointer(pass + 4), true))
                .Append("  }\n  pixelShader ")
                .Append(pixel).Append("\n  {\n")
                .Append(Iw3PcShaderArgumentReader.ReadArguments(zone, pass, zone.Pointer(pass + 8), false))
                .Append("  }\n")
                .Append(Iw3PcShaderArgumentReader.ReadRouting(zone, pass))
                .Append("}\n");
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static bool ValidShader(Iw3PcZone zone, int shader, bool vertex)
    {
        if (shader == 0) return false;
        int count = U16(zone, shader + 12);
        int program = zone.Pointer(shader + 8);
        if (program == 0 || count < 2) return false;
        uint profile = U32(zone, program);
        return profile >> 16 == (vertex ? 0xfffeu : 0xffffu) &&
            (profile >> 8 & 255) is >= 1 and <= 3 &&
            U32(zone, program + (count - 1) * 4) == 0x0000ffff;
    }

    private string ReadShader(int shader, bool vertex, Action<string, byte[]> write)
    {
        if (!ValidShader(zone, shader, vertex))
            throw new InvalidDataException("IW3 PC technique has an invalid shader program.");
        int program = zone.Pointer(shader + 8);
        uint profile = U32(zone, program);
        string qualified = "sm" + (profile >> 8 & 255) + "_" + (profile & 255) + "/" + zone.String(shader);
        string path = "shader_bin/" + (vertex ? "vs_" : "ps_") + qualified + ".cso";
        int length = checked(U16(zone, shader + 12) * 4);
        if (programs.TryGetValue(path, out var previous))
        {
            if (previous.Length != length || !zone.Bytes(previous.Offset, length).SequenceEqual(zone.Bytes(program, length)))
                throw new InvalidDataException($"Conflicting IW3 technique output '{path}'.");
        }
        else
        {
            write(path, zone.Bytes(program, length).ToArray());
            programs.Add(path, (program, length));
        }
        return (profile >> 8 & 255) + "." + (profile & 255) + " \"" + qualified + "\"";
    }

    private static int U16(Iw3PcZone zone, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(offset, 2));
    private static uint U32(Iw3PcZone zone, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes(offset, 4));
}
