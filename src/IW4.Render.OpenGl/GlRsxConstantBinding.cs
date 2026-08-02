using System.Numerics;
using Silk.NET.OpenGL;

using IW4.Render.Shaders;

namespace IW4.Render.OpenGl;

internal readonly record struct GlRsxConstantBinding(
    int Location,
    float? X,
    float? Y,
    float? Z,
    float? W,
    MapRenderCodeMatrixSemantic? CodeMatrixSemantic,
    MapRenderCodeMatrixTransform CodeMatrixTransform,
    int CodeMatrixRow,
    ushort? DynamicCodeConstantSourceRow = null,
    int? SceneLightIndex = null);
