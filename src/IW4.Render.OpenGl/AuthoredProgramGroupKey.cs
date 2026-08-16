using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

internal readonly record struct AuthoredProgramGroupKey(
    string MaterialName,
    string TechniqueSetName,
    int TechniqueSlot,
    string TechniqueName,
    byte SceneLightIndex);
