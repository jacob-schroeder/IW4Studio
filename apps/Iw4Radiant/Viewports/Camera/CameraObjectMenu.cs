using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraObjectMenu
{
    internal static ContextMenu Open(CameraViewport viewport, EditorSession session,
        IReadOnlyList<(object Item, string Label)> hits, BrushFaceSelection? target,
        Action<BrushKind> classify, Action createModelPlayerClip, Action<string> status)
    {
        MapDocument document = session.Document;
        var menu = new ContextMenu();
        bool openedForPlacement = viewport.PhysicsPlacementActive;
        var entries = new List<(object Item, MenuItem Menu)>();
        var kinds = new MenuItem { Header = "Brush type" };
        var playerClip = new MenuItem { Header = "Create player clip from models" };
        ToolTip.SetTip(playerClip, "Fit an editable outer hull to each selected model, simplifying dense shapes. Holes and concave spaces are filled; adjust the brushes as needed.");
        playerClip.Click += (_, _) => { if (IsCurrent()) createModelPlayerClip(); };
        var selectAll = new MenuItem { Header = "Select all hit objects", StaysOpenOnClick = true };
        var deselectAll = new MenuItem { Header = "Deselect all hit objects", StaysOpenOnClick = true };
        var physics = new MenuItem { Header = "Physics" };
        var drop = new MenuItem();
        var selectionHint = new MenuItem
        {
            Header = "Select model props or prefabs, or one model with its player clips", IsEnabled = false
        };
        var reset = new MenuItem { Header = "Reset" };
        var keep = new MenuItem { Header = "Keep placement" };
        var cancel = new MenuItem { Header = "Cancel" };
        physics.Items.Add(drop);
        physics.Items.Add(selectionHint);
        physics.Items.Add(reset);
        physics.Items.Add(keep);
        physics.Items.Add(cancel);
        drop.Click += async (_, _) =>
        {
            if (!IsCurrent()) return;
            if (!viewport.PhysicsPlacementActive) await viewport.StartPhysicsPlacementAsync();
            else if (viewport.PhysicsPlacementRunning) viewport.PausePhysicsPlacement();
            else viewport.ResumePhysicsPlacement();
        };
        reset.Click += (_, _) => { if (IsCurrent()) viewport.ResetPhysicsPlacement(); };
        keep.Click += (_, _) => { if (IsCurrent()) viewport.ApplyPhysicsPlacement(); };
        cancel.Click += (_, _) => { if (IsCurrent()) viewport.StopPhysicsPlacement(); };
        viewport.PhysicsPlacementChanged += RefreshPhysics;
        menu.Closed += (_, _) => viewport.PhysicsPlacementChanged -= RefreshPhysics;
        if (openedForPlacement)
        {
            menu.Items.Add(physics);
            RefreshPhysics();
            menu.Open(viewport);
            return menu;
        }
        if (session.Selection.Count == 1 && session.Selection.Active is MapBrush selected &&
            target is { } face && !ReferenceEquals(selected, face.Brush))
        {
            var extend = new MenuItem { Header = "Extend selected brush to this face" };
            ToolTip.SetTip(extend, "Moves the opposing plane. Keeps the selected brush's materials and texture projections.");
            extend.Click += (_, _) =>
            {
                if (!IsCurrent() || !ReferenceEquals(session.Selection.Active, selected) || session.Selection.Count != 1 ||
                    !face.Brush.Faces.Contains(face.Face)) return;
                try
                {
                    MapBrush result = BrushGeometry.ExtendToFace(selected, face.Face);
                    session.Edit(() => selected.ReplaceFaces(result));
                    status("Extended brush to face. Materials and texture projections kept.");
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
                { status(exception.Message); }
            };
            menu.Items.Add(extend);
            menu.Items.Add(new Separator());
        }
        foreach (var (item, label) in hits)
        {
            var entry = new MenuItem
            {
                Header = new TextBlock { Text = $"{entries.Count + 1}. {label}" },
                ToggleType = MenuItemToggleType.CheckBox,
                StaysOpenOnClick = true
            };
            entry.Click += (_, _) =>
            {
                if (IsCurrent()) session.Select(item, additive: true, toggle: true);
                Refresh();
            };
            entries.Add((item, entry));
            menu.Items.Add(entry);
        }
        if (hits.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No selectable objects here", IsEnabled = false });
        menu.Items.Add(new Separator());
        selectAll.Click += (_, _) =>
        {
            if (IsCurrent()) session.SelectRange(session.Selection.Items.Concat(hits.Select(hit => hit.Item)));
            Refresh();
        };
        deselectAll.Click += (_, _) =>
        {
            if (IsCurrent())
            {
                var objects = new HashSet<object>(hits.Select(hit => hit.Item), ReferenceEqualityComparer.Instance);
                session.SelectRange(session.Selection.Items.Where(item => !objects.Contains(item)));
            }
            Refresh();
        };
        menu.Items.Add(selectAll);
        menu.Items.Add(deselectAll);
        menu.Items.Add(new Separator());
        foreach (var (label, kind) in new[]
                 {
                     ("Structural", BrushKind.Structural), ("Detail", BrushKind.Detail),
                     ("Non Collide", BrushKind.NonColliding), ("Weapon Clip", BrushKind.WeaponClip)
                 })
        {
            var entry = new MenuItem { Header = label };
            entry.Click += (_, _) => { if (IsCurrent()) classify(kind); };
            kinds.Items.Add(entry);
        }
        menu.Items.Add(kinds);
        menu.Items.Add(playerClip);
        menu.Items.Add(new Separator());
        menu.Items.Add(physics);
        Refresh();
        menu.Open(viewport);
        return menu;

        bool IsCurrent() => ReferenceEquals(viewport.Session, session) &&
            ReferenceEquals(document, session.Document);

        void Refresh()
        {
            foreach (var (item, entry) in entries)
            {
                entry.IsChecked = IsCurrent() && session.Selection.Contains(item);
                entry.IsEnabled = IsCurrent() && session.Visibility.CanSelect(document, item);
            }
            selectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && !entry.Menu.IsChecked);
            deselectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && entry.Menu.IsChecked);
            kinds.IsEnabled = IsCurrent() && session.Selection.Items.Select(EditorSelection.Owner).OfType<MapBrush>()
                .Any(brush => session.Visibility.CanSelect(document, brush));
            playerClip.IsVisible = playerClip.IsEnabled = IsCurrent() && PlayerClipEditing.CanGenerateFromModels(session);
            RefreshPhysics();
        }

        void RefreshPhysics()
        {
            bool active = viewport.PhysicsPlacementActive;
            if (openedForPlacement && (!active || !IsCurrent()))
            {
                menu.Close();
                return;
            }
            bool canStart = CanStartPhysics();
            physics.IsEnabled = IsCurrent();
            drop.Header = active && viewport.PhysicsPlacementRunning ? "Pause" :
                active && viewport.PhysicsPlacementHasStarted ? "Resume" : "Drop";
            drop.IsEnabled = physics.IsEnabled && (active
                ? !viewport.PhysicsPlacementPreparing && !viewport.PhysicsPlacementNeedsReset &&
                  !viewport.PhysicsPlacementSettled
                : canStart);
            selectionHint.IsVisible = !active && !canStart;
            reset.IsVisible = keep.IsVisible = cancel.IsVisible = active;
            reset.IsEnabled = IsCurrent() && active && !viewport.PhysicsPlacementPreparing &&
                (viewport.PhysicsPlacementHasStarted || viewport.PhysicsPlacementNeedsReset);
            keep.IsEnabled = IsCurrent() && active && viewport.PhysicsPlacementHasChanges &&
                !viewport.PhysicsPlacementNeedsReset;
            cancel.IsEnabled = IsCurrent() && active;
        }

        bool CanStartPhysics()
        {
            if (session.HasPlacement || viewport.FoliagePaintingEnabled) return false;
            return CameraPrefabPlacementSimulation.CanStart(session);
        }
    }
}
