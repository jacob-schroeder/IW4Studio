using System.Runtime.InteropServices;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PC.Shaders;

internal static class RsxShaderLibrary
{
    private const string Library = "iw4_map_shaders";

    internal static (string Assembly, string Profile) Translate(byte[] bytecode, Iw3ShaderStage stage, string path)
    {
        string? assembly = TryTranslate(bytecode, "nv3", out string? error);
        if (assembly is not null)
            return (assembly, "nv3");

        // TEXLDL in IW3 pixel shaders requires the NV4 profile.
        if (stage == Iw3ShaderStage.Pixel)
        {
            assembly = TryTranslate(bytecode, "nv4", out string? fallbackError);
            if (assembly is not null)
                return (assembly, "nv4");
            error += $"{Environment.NewLine}nv4: {fallbackError}";
        }
        throw new InvalidDataException($"Shader '{path}' could not be translated from Direct3D 9 bytecode. nv3: {error}");
    }

    private static string? TryTranslate(byte[] bytecode, string profile, out string? error)
    {
        nint output = 0, diagnostic = 0;
        try
        {
            int status = TranslateD3d9(profile, bytecode, (uint)bytecode.Length, out output, out diagnostic);
            error = status == 0 ? null : ReadDiagnostic(diagnostic);
            return status == 0 ? Marshal.PtrToStringUTF8(output) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException($"Could not load the packaged {Library} shader library for {RuntimeInformation.RuntimeIdentifier}. {ex.Message}", ex);
        }
        finally
        {
            if (output != 0) Free(output);
            if (diagnostic != 0) Free(diagnostic);
        }
    }

    internal static byte[] Assemble(string assembly, Iw3ShaderStage stage, string path)
    {
        nint output = 0, diagnostic = 0;
        try
        {
            int status = AssembleRsx(assembly, stage == Iw3ShaderStage.Pixel ? 1 : 0,
                out output, out nuint length, out diagnostic);
            if (status != 0)
                throw new InvalidDataException($"Shader '{path}' could not be assembled for RSX. {ReadDiagnostic(diagnostic)}");
            byte[] program = new byte[checked((int)length)];
            Marshal.Copy(output, program, 0, program.Length);
            return program;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException($"Could not load the packaged {Library} shader library for {RuntimeInformation.RuntimeIdentifier}. {ex.Message}", ex);
        }
        finally
        {
            if (output != 0) Free(output);
            if (diagnostic != 0) Free(diagnostic);
        }
    }

    private static string ReadDiagnostic(nint diagnostic) =>
        Marshal.PtrToStringUTF8(diagnostic) ?? "The shader library returned no diagnostic.";

    [DllImport(Library, EntryPoint = "iw4_translate_d3d9", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern int TranslateD3d9([MarshalAs(UnmanagedType.LPUTF8Str)] string profile,
        byte[] bytecode, uint length, out nint output, out nint diagnostic);

    [DllImport(Library, EntryPoint = "iw4_assemble_rsx", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern int AssembleRsx([MarshalAs(UnmanagedType.LPUTF8Str)] string assembly, int pixel,
        out nint output, out nuint length, out nint diagnostic);

    [DllImport(Library, EntryPoint = "iw4_shader_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static extern void Free(nint buffer);
}
