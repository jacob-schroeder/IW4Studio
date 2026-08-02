using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public enum MaterialShaderArgumentType : ushort
{
    MaterialVertexConst = 0x0,
    LiteralVertexConst = 0x1,
    MaterialPixelSampler = 0x2,
    CodePrimBegin = 0x3,
    CodeVertexConst = 0x3,
    CodePixelSampler = 0x4,
    CodePixelConst = 0x5,
    MaterialPixelConst = 0x6,
    CodePrimEnd = 0x6,
    LiteralPixelConst = 0x7
}
