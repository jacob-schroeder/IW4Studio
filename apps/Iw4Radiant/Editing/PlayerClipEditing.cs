using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal static class PlayerClipEditing
{
    internal static bool CanApply(EditorSession session) => session.Selection.Count > 0 &&
        session.Selection.Items.All(item => item is MapBrush brush && session.Document.World.Brushes.Contains(brush) &&
            session.Visibility.CanSelect(session.Document, brush));

    internal static void Apply(EditorSession session)
    {
        if (!CanApply(session))
            throw new ArgumentException("Select whole world brushes to make player clip. Draw and shape a separate brush around the area that should block players.");
        SelectionEditing.ApplyMaterial(session, ClipBrushMaterial.PlayerClip);
    }
}
