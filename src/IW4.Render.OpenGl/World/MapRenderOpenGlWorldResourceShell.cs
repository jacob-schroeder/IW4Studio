namespace IW4.Render.OpenGl.World;

/// <summary>
/// Enforces the two-phase world resource contract: texture, program, and
/// state resources are prepared per batch, while all geometry is owned by a
/// final packed arena.
/// </summary>
internal static class MapRenderOpenGlWorldResourceShell
{
    internal static GlTexturedMesh RequireGeometryFree(
        GlTexturedMesh resource)
    {
        if (resource.VertexArray != 0 ||
            resource.VertexBuffer != 0 ||
            resource.ElementBuffer != 0 ||
            resource.InstanceBuffer != 0 ||
            resource.IndexOffsetBytes != 0 ||
            resource.BaseVertex != 0 ||
            resource.OwnsGeometry)
        {
            throw new InvalidOperationException(
                "A world resource shell must not allocate or own geometry before arena packing.");
        }

        return resource;
    }
}
