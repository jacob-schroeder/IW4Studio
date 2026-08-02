using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Picking;

public readonly record struct MapRenderPickCandidate(
    MapRenderPickHit Hit,
    MapRenderPickCandidateLayer Layer,
    int DistanceRank,
    int Priority,
    bool IsTextured,
    bool IsCameraColorCandidate,
    bool IsFallbackMaterialCandidate,
    bool HasColorSemantic,
    bool HasNonDegenerateUv,
    float UvArea,
    string Reason);
