using System.Runtime;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Releases temporary managed workspaces at one-time map-render lifecycle
/// boundaries. Scene construction, OpenGL resource initialization, and the
/// first retained render packet each materialize long-lived output from much
/// larger transient collections and staging arrays. Steady rendering then
/// allocates too little to promptly trigger the full collection that would
/// otherwise decommit those regions on a unified-memory system.
/// </summary>
internal static class RenderBuildMemoryReclaimer
{
    private static readonly Lock ReclamationLock = new();

    internal static void ReclaimCompletedBuildWorkspace()
    {
        lock (ReclamationLock)
        {
            GCSettings.LargeObjectHeapCompactionMode =
                GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Aggressive,
                blocking: true,
                compacting: true);
        }
    }
}
