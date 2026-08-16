using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

internal readonly record struct PixelTextureOp(
    int TextureUnit,
    int InstructionIndex,
    RsxFragmentOperand CoordinateOperand,
    RsxFragmentInputAttribute EncodedSourceAttribute);
