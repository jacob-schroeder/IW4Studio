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

/// <summary>
/// Identifies one instanced static-model pass. <see cref="SelectedTechniqueSlot"/>
/// preserves the effective normal-camera selector (the per-instance page/light
/// row or the draw method's emissive phase) independently of the pass that is
/// executable today, so fallback materialization cannot merge distinct native
/// phase populations. <see cref="ReflectionProbeIndex"/>
/// is populated only for a selected technique group that consumes the native
/// reflection-probe sampler, preserving batching density for every other group.
/// </summary>
internal readonly record struct StaticTexturedBatchKey(
    int LodIndex,
    XSurface Surface,
    MaterialAsset Material,
    int? SelectedTechniqueSlot,
    int TechniqueSlot,
    int PassIndex,
    int SamplerArgIndex,
    uint SamplerHash,
    byte? ReflectionProbeIndex,
    byte SceneLightIndex = 0);
