using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XModel;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Render.Geometry;

internal readonly record struct StaticModelPlacement(
    Vector3 Origin,
    Vector3 Axis0,
    Vector3 Axis1,
    Vector3 Axis2);
