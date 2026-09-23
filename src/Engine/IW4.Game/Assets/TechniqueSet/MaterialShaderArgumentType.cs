using IW4.Game.Assets;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.TechniqueSet;

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
