namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Runtime clip-plane equation consumed by the PS3 static culler:
/// dot(normal, point) + coefficientW is positive on the retained side.
/// </summary>
public readonly record struct MapRenderWorldDpvsClipPlane(
    float NormalX,
    float NormalY,
    float NormalZ,
    float CoefficientW);
