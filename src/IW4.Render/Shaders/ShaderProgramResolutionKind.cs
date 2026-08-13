namespace IW4.Render.Shaders;

public enum ShaderProgramResolutionKind
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
