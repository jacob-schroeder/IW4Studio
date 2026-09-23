namespace IW4.Render.Scheduling.Dpvs;

internal readonly record struct MapRenderWorldDpvsBounds(
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ)
{
    public bool IsValid =>
        float.IsFinite(MinX) &&
        float.IsFinite(MinY) &&
        float.IsFinite(MinZ) &&
        float.IsFinite(MaxX) &&
        float.IsFinite(MaxY) &&
        float.IsFinite(MaxZ) &&
        MinX <= MaxX &&
        MinY <= MaxY &&
        MinZ <= MaxZ;
}
