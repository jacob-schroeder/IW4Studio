using IW4.Formats.SourceFormat.Physics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class RuntimePhysicsEditing
{
    internal static bool CanSet(EditorSession session, MapEntity entity, bool enabled, out string reason)
    {
        if (!session.Document.Entities.Contains(entity))
        {
            reason = "The model is no longer in the current map.";
            return false;
        }
        if (entity.ClassName != (enabled ? "misc_model" : RuntimePhysicsAuthoring.ClassName))
        {
            reason = enabled ? "Select a static misc_model to enable runtime physics."
                : "Select a dyn_model to disable runtime physics.";
            return false;
        }
        if (!enabled)
        {
            reason = "";
            return true;
        }
        if (entity.Brushes.Count != 0 || entity.Terrains.Count != 0 || entity.PreservedPrimitives.Count != 0)
        {
            reason = "Runtime physics models cannot own brushes, terrain, or other map primitives.";
            return false;
        }
        try
        {
            RuntimePhysicsAuthoring.Validate(entity.Properties);
            reason = "";
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    internal static void Set(EditorSession session, MapEntity entity, bool enabled)
    {
        if (!CanSet(session, entity, enabled, out string reason)) throw new ArgumentException(reason);
        session.Edit(() => entity.Properties["classname"] = enabled ? RuntimePhysicsAuthoring.ClassName : "misc_model");
    }
}
