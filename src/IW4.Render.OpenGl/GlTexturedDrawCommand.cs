namespace IW4.Render.OpenGl;

/// <summary>
/// Draws either an entire textured mesh or one instance from its shared
/// instance buffer. The latter keeps blended static models sortable without
/// duplicating geometry or textures. A nonnegative world-batch index links an
/// editor world command to its reusable per-surface visible-run storage.
/// </summary>
internal readonly record struct GlTexturedDrawCommand(
    GlTexturedMesh Mesh,
    int? InstanceIndex = null,
    int WorldBatchIndex = -1,
    int WorldReceiverChannelIndex = -1);
