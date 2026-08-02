using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Enforces the native XModel registration callback's destination-only
/// VERTEX arena contract. The callback stores one four-byte runtime pair for
/// every surface in each owned XModel.
/// </summary>
internal static class OwnedXModelVertexRuntimeArenaValidator
{
    private const int RuntimeSurfacePairSize = sizeof(ushort) * 2;

    internal static int GetRequiredByteCount(IXModelBuildData model) =>
        checked(model.NumSurfs * RuntimeSurfacePairSize);

    internal static void Reserve(
        IXModelBuildData model,
        EmissionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);

        int byteCount = GetRequiredByteCount(model);
        if (byteCount == 0)
            return;

        plan.Push(
            XFileBlockType.VERTEX,
            "owned XModel runtime surface pairs");
        try
        {
            plan.AllocateWithoutSource(
                byteCount,
                sizeof(int),
                "XModel",
                "runtimeSurfacePairs");
        }
        finally
        {
            plan.Pop(XFileBlockType.VERTEX);
        }
    }
}
