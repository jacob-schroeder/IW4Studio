using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Narrow interaction boundary between the native viewport and a contextual
/// translation editor. Candidate transforms are transient; only
/// <see cref="ApplyChanges"/> may create a semantic history entry.
/// </summary>
public interface IWorldViewportTranslationTool
{
    event EventHandler? DraftChanged;

    MapObjectId TargetObjectId { get; }

    /// <summary>
    /// Imported Gfx static-model row affected by this draft, when one exists.
    /// Source-authored geometry has no compiled render row and returns null.
    /// </summary>
    int? RenderStaticModelSourceOrdinal { get; }

    MapVector3 DraftOrigin { get; }

    MapBounds? Bounds { get; }

    bool CanManipulate { get; }

    bool HasDraftChanges { get; }

    void BeginManipulation();

    void UpdateDraftOrigin(MapVector3 origin);

    void EndManipulation();

    void ApplyChanges();

    void CancelChanges();
}
