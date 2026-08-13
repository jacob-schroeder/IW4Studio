using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public enum MaterialShaderArgumentType : ushort
{
    MaterialVertexConst = 0x0, // stable
    LiteralVertexConst = 0x1, // stable
    MaterialPixelSampler = 0x2, // stable
    CodePrimBegin = 0x3,
    CodeVertexConst = 0x3, // stable object prim
    CodePixelSampler = 0x4, // stable object
    CodePixelConst = 0x5, // stable
    MaterialPixelConst = 0x6, // stable
    CodePrimEnd = 0x6, // stable
    LiteralPixelConst = 0x7 // stable
}
