namespace IW4.Render.Shaders;

public enum MapRenderShaderProgramResolutionKind
{
    Unresolved = 0,
    CanonicalActiveProvider,
    AliasCellOwner,
    PersistentBlockAddress,
    HydratedActiveProvider,
    UniqueNameFallback,
    HydratedObjectWithoutActiveProvider,
    NamedPlaceholder
}
