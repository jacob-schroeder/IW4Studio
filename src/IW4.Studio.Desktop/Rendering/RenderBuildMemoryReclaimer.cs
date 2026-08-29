using System.Runtime;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Releases temporary managed map-build workspaces once renderer startup has
/// settled. Scene construction and native resource initialization retain a
/// relatively small result from much larger transient collections; steady
/// rendering may then allocate too little to make the runtime compact those
/// regions promptly.
/// </summary>
internal static class RenderBuildMemoryReclaimer
{
    private const long MinimumFragmentedBytes = 64L * 1024 * 1024;
    private const int MinimumFragmentedHeapPercent = 12;
    private const int HighMemoryLoadPercent = 80;
    private static readonly Lock ReclamationLock = new();

    /// <summary>
    /// Compacts only after the renderer has presented and released its startup
    /// staging owners, and only when the GC reports material fragmentation or
    /// host memory pressure. Unconditional collections at the scene, resource,
    /// and first-frame boundaries previously stopped the UI three times while
    /// often finding the same long-lived scene graph.
    /// </summary>
    internal static bool TryReclaimSettledBuildWorkspace()
    {
        lock (ReclamationLock)
        {
            GCMemoryInfo memory = GC.GetGCMemoryInfo();
            long heapBytes = Math.Max(0, memory.HeapSizeBytes);
            long fragmentedBytes = Math.Max(0, memory.FragmentedBytes);
            bool materiallyFragmented =
                fragmentedBytes >= MinimumFragmentedBytes &&
                heapBytes > 0 &&
                fragmentedBytes * 100 >=
                    heapBytes * MinimumFragmentedHeapPercent;
            bool underMemoryPressure =
                memory.HighMemoryLoadThresholdBytes > 0 &&
                memory.MemoryLoadBytes * 100 >=
                    memory.HighMemoryLoadThresholdBytes *
                    HighMemoryLoadPercent;
            if (!materiallyFragmented && !underMemoryPressure)
                return false;

            GCSettings.LargeObjectHeapCompactionMode =
                GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Aggressive,
                blocking: true,
                compacting: true);
            return true;
        }
    }
}
