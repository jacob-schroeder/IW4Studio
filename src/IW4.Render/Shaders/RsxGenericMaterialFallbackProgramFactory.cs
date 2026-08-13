using System.Collections.Immutable;
using System.Text;

namespace IW4.Render.Shaders;

/// <summary>
/// Backend-neutral identity for the editor's generic material fallback. This
/// is a deliberately explicit preview program: it is not presented as an
/// authored RSX translation, but it gives backends one stable shader contract
/// for materials such as shadow-only and flare assets that have no camera-color
/// technique pass.
/// </summary>
public sealed class RsxGenericMaterialFallbackPrograms
{
    internal RsxGenericMaterialFallbackPrograms(
        RsxVertexProgramIr vertexProgram,
        RsxFragmentProgramIr fragmentProgram)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        ArgumentNullException.ThrowIfNull(fragmentProgram);
        VertexProgram = vertexProgram;
        FragmentProgram = fragmentProgram;
    }

    public RsxVertexProgramIr VertexProgram { get; }

    public RsxFragmentProgramIr FragmentProgram { get; }
}

/// <summary>
/// Creates the stable IR marker pair consumed by the Vulkan generic-material
/// lowering path. The IR carries sampler provenance for the shared scene
/// contract; Vulkan owns the actual preview SPIR-V implementation.
/// </summary>
public static class RsxGenericMaterialFallbackProgramFactory
{
    private const string VertexMarker =
        "iw4.gmf.v1";
    private const string FragmentMarker =
        "iw4.gmf.f1";

    private static readonly Lazy<RsxGenericMaterialFallbackPrograms> Cached =
        new(CreatePrograms);

    public static RsxGenericMaterialFallbackPrograms Create() => Cached.Value;

    public static bool IsVertex(RsxVertexProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return HasMarker(program.InputProgramBytes, VertexMarker);
    }

    public static bool IsFragment(RsxFragmentProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return HasMarker(program.OriginalProgramBytes, FragmentMarker);
    }

    private static RsxGenericMaterialFallbackPrograms CreatePrograms() =>
        new(CreateVertexProgram(), CreateFragmentProgram());

    private static RsxVertexProgramIr CreateVertexProgram()
    {
        byte[] bytes = CreateMarkerBytes(VertexMarker, 16);
        return new RsxVertexProgramIr(
            bytes,
            RsxVertexProgramIr.CurrentDecoderVersion,
            uploadOffset: 0,
            ImmutableArray<RsxVertexInstruction>.Empty);
    }

    private static RsxFragmentProgramIr CreateFragmentProgram()
    {
        byte[] bytes = CreateMarkerBytes(FragmentMarker, 32);
        const uint destination =
            1u | // end
            (0x0fu << 9) |
            (1u << 13) |
            (0x17u << 24); // texture2D, sampler destination zero
        var instruction = new RsxFragmentInstruction(
            Index: 0,
            Offset: 0,
            Dst: destination,
            Src0: 2u,
            Src1: 0,
            Src2: 0,
            Opcode: 0x17,
            ByteCount: 32,
            Constant: null);

        return new RsxFragmentProgramIr(
            bytes,
            bytes,
            RsxFragmentProgramIr.CurrentDecoderVersion,
            RsxFragmentProgramIr.CurrentSemanticTranslationVersion,
            uploadOffset: 0,
            uploadSize: bytes.Length,
            ImmutableArray.Create(instruction),
            Array.Empty<StaticFragmentConstantPatch>(),
            Array.Empty<CodePixelConstantPatchPlan>(),
            new RsxFragmentProgramControl(
                IsValid: true,
                DescriptorOffset: 0,
                RegisterCount: 2,
                ExportPrecisionRaw: 1,
                DepthExportRaw: 0,
                ControlFlagsRaw: 0,
                EmittedControl: 0x02008400u),
            new RsxFragmentSamplerFeatureProfile(
                new HashSet<int>(),
                new HashSet<int>(),
                new HashSet<int>()),
            [
                new RsxFragmentColorExport(0, true, 0, 0x0f, "xyzw"),
                new RsxFragmentColorExport(1, true, 4, 0, string.Empty),
                new RsxFragmentColorExport(2, true, 6, 0, string.Empty),
                new RsxFragmentColorExport(3, true, 8, 0, string.Empty)
            ]);
    }

    private static byte[] CreateMarkerBytes(string marker, int length)
    {
        byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
        if (markerBytes.Length > length)
            throw new InvalidOperationException("Generic fallback marker is too long.");
        byte[] bytes = new byte[length];
        markerBytes.CopyTo(bytes, 0);
        return bytes;
    }

    private static bool HasMarker(
        ImmutableArray<byte> bytes,
        string marker)
    {
        byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
        return bytes.Length >= markerBytes.Length &&
            bytes.AsSpan(0, markerBytes.Length).SequenceEqual(markerBytes);
    }
}
