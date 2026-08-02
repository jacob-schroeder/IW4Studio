using IW4.Runtime.Database;
namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Loader composition for creating a concrete PS3-shaped load context from
/// Runtime-owned registry state. Runtime itself remains independent of loader
/// mechanics and backend packages.
/// </summary>
public static class DbRuntimeLoaderExtensions
{
    public static DbLoadContext CreateLoadContext(this DbRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.ThrowIfFaulted();

        return new DbLoadContext(
            runtime.AssetPool,
            runtime.ScriptStrings,
            runtime.MaterialTechniqueStateCache,
            runtime.GfxImageRuntimeRegistrationHooks,
            runtime.AssetRuntimeLifecycle);
    }
}
