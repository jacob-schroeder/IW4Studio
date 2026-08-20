using System.Text;
using IW4.Render.Shaders;

namespace IW4.Render.Metal.Shaders;

/// <summary>
/// Stable resource and stage interface shared by directly lowered RSX Metal
/// shaders. Vertex inputs are a vertex-major array of sixteen float4 values.
/// </summary>
internal static class MetalRsxShaderAbi
{
    internal const int VertexInputBufferIndex = 0;
    internal const int VertexConstantBufferIndex = 1;
    internal const int StaticInstanceBufferIndex = 2;
    internal const int FrameVertexConstantBufferIndex = 3;
    internal const int StaticCompositionBufferIndex = 4;
    internal const int FragmentCodeConstantBufferIndex = 0;
    internal const int FragmentStaticConstantBufferIndex = 1;
    internal const int TextureDestinationCount = 16;
    internal const int StaticPlacementFloat4Stride = 3;
    internal const int StaticLightingPlacementFloat4Stride = 4;
    internal const int VertexInputFloat4Count = 16;
    internal const int FrameMatrixRowCount = 96;
    internal const int FrameGameTimeRow = FrameMatrixRowCount;
    internal const int FrameClipScaleRow = FrameGameTimeRow + 1;
    internal const int FrameClipOffsetRow = FrameClipScaleRow + 1;
    internal const int FrameZNearRow = FrameClipOffsetRow + 1;
    internal const int FrameEyeOffsetRow = FrameZNearRow + 1;
    internal const int FrameVegetationTimeRow = FrameEyeOffsetRow + 1;
    internal const int FrameVertexFloat4Count = FrameVegetationTimeRow + 1;
    internal const string VertexEntryPoint = "rsxVertexMain";
    internal const string FragmentEntryPoint = "rsxFragmentMain";

    internal static void AppendPreamble(StringBuilder builder)
    {
        builder.AppendLine("#include <metal_stdlib>");
        builder.AppendLine("using namespace metal;");
        builder.AppendLine("#ifndef IW4_RSX_STAGE_ABI");
        builder.AppendLine("#define IW4_RSX_STAGE_ABI");
        builder.AppendLine("struct RsxVertexStageOut");
        builder.AppendLine("{");
        builder.AppendLine("  float4 position [[position]];");
        builder.AppendLine("  float4 color0 [[user(rsx_color0)]];");
        builder.AppendLine("  float4 color1 [[user(rsx_color1)]];");
        for (int i = 0; i < 8; i++)
        {
            builder.AppendLine(
                $"  float4 texcoord{i} [[user(rsx_texcoord{i})]];");
        }
        builder.AppendLine("};");
        builder.AppendLine("#endif");
    }

    internal static void AppendVertexConstantLayout(StringBuilder builder)
    {
        builder.AppendLine("#ifndef IW4_RSX_VERTEX_CONSTANT_ABI");
        builder.AppendLine("#define IW4_RSX_VERTEX_CONSTANT_ABI");
        builder.AppendLine("struct RsxVertexConstants");
        builder.AppendLine("{");
        builder.AppendLine(
            $"  float4 values[{RsxVertexConstantLayout.Count}];");
        builder.AppendLine("};");
        builder.AppendLine("#endif");
    }

    internal static void AppendStaticModelLayouts(StringBuilder builder)
    {
        builder.AppendLine("struct RsxMapFrameVertexConstants");
        builder.AppendLine("{");
        builder.AppendLine($"  float4 matrixRows[{FrameMatrixRowCount}];");
        builder.AppendLine("  float4 gameTime;");
        builder.AppendLine("  float4 clipSpaceLookupScale;");
        builder.AppendLine("  float4 clipSpaceLookupOffset;");
        builder.AppendLine("  float4 zNear;");
        builder.AppendLine("  float4 eyeOffset;");
        builder.AppendLine("  float4 vegetationTime;");
        builder.AppendLine("};");
        builder.AppendLine("struct RsxStaticCompositionConstants");
        builder.AppendLine("{");
        builder.AppendLine("  float4 parameters;");
        builder.AppendLine("  float4 bounds;");
        builder.AppendLine("};");
    }
}
