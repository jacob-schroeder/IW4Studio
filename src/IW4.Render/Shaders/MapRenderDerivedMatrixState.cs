using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace IW4.Render.Shaders;

internal readonly record struct MapRenderDerivedMatrixState(
    Matrix4x4 View,
    Matrix4x4 Projection,
    Matrix4x4 ViewProjection,
    Matrix4x4 World0,
    Matrix4x4 WorldView0,
    Matrix4x4 WorldViewProjection0,
    Vector3 EyeOffset,
    Matrix4x4? ShadowLookup = null);
