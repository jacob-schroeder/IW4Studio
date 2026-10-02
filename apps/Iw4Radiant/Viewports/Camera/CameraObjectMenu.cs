using Avalonia;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Viewports;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraObjectMenu
{
    internal static ContextMenu Open(CameraViewport viewport, EditorSession session,
        IReadOnlyList<(object Item, string Label)> hits, BrushFaceSelection? target, Point position,
        Action<BrushKind> classify, Action water, Action createModelPlayerClip, Action<MapEntity> inspectEntity,
        Action<MapEntity> showScript, Action<string> status)
    {
        MapDocument document = session.Document;
        var menu = new ContextMenu();
        bool openedForSimulation = viewport.PhysicsPlacementActive || viewport.GlassShatterActive;
        var entries = new List<(object Item, MenuItem Menu)>();
        var kinds = new MenuItem { Header = "Brush type" };
        var waterItem = new MenuItem { Header = "Water" };
        ToolTip.SetTip(waterItem, "Apply water to all selected brushes.");
        waterItem.Click += (_, _) => { if (IsCurrent()) water(); };
        var playerClip = new MenuItem { Header = "Create player clip from models" };
        var script = new MenuItem { Header = "Show script" };
        script.Click += (_, _) =>
        {
            if (IsCurrent() && session.Selection.Active is { } active &&
                MapOrganization.Entity(document, active) is { } entity && document.Entities.Contains(entity)) showScript(entity);
        };
        ToolTip.SetTip(playerClip, "Fit editable brushes to solid collision geometry, using smaller sections where the mesh supports them. Models without collision source use an outer visual hull. Review and adjust the result.");
        playerClip.Click += (_, _) => { if (IsCurrent()) createModelPlayerClip(); };
        var selectAll = new MenuItem { Header = "Select all hit objects", StaysOpenOnClick = true };
        var deselectAll = new MenuItem { Header = "Deselect all hit objects", StaysOpenOnClick = true };
        var physics = new MenuItem { Header = "Physics" };
        var physicsMenuSeparator = new Separator();
        var enableRuntimePhysics = new MenuItem { Header = "Enable runtime physics" };
        var disableRuntimePhysics = new MenuItem { Header = "Disable runtime physics" };
        var runtimePhysicsSeparator = new Separator();
        var drop = new MenuItem();
        var reset = new MenuItem { Header = "Reset" };
        var keep = new MenuItem { Header = "Keep placement" };
        var cancel = new MenuItem { Header = "Cancel" };
        var shatter = new MenuItem { Header = "Shatter" };
        ToolTip.SetTip(shatter, "Preview a break at this point on the selected glass. Panes away from this point break at their center.");
        var shatterStop = new MenuItem { Header = "Restore glass" };
        var destructible = new MenuItem { Header = "Destructible" };
        var destructiblePreview = new MenuItem { Header = "Preview destruction…" };
        var destructibleReset = new MenuItem { Header = "Reset preview" };
        destructible.Items.Add(destructiblePreview);
        destructible.Items.Add(destructibleReset);
        destructiblePreview.Click += (_, _) =>
        {
            if (IsCurrent() && DestructibleTarget() is { } entity) viewport.RequestDestructiblePreview(entity);
        };
        destructibleReset.Click += (_, _) => { if (IsCurrent()) viewport.RequestDestructiblePreview(null); };
        physics.Items.Add(enableRuntimePhysics);
        physics.Items.Add(disableRuntimePhysics);
        physics.Items.Add(runtimePhysicsSeparator);
        physics.Items.Add(drop);
        physics.Items.Add(reset);
        physics.Items.Add(keep);
        physics.Items.Add(cancel);
        physics.Items.Add(shatter);
        physics.Items.Add(shatterStop);
        enableRuntimePhysics.Click += (_, _) => SetRuntimePhysics(true);
        disableRuntimePhysics.Click += (_, _) => SetRuntimePhysics(false);
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
        shatter.Click += async (_, _) => { if (IsCurrent()) await viewport.StartGlassShatterAsync(position); };
        shatterStop.Click += (_, _) => { if (IsCurrent()) viewport.StopGlassShatter(); };
        viewport.PhysicsPlacementChanged += RefreshPhysics;
        viewport.GlassShatterChanged += RefreshPhysics;
        menu.Closed += (_, _) =>
        {
            viewport.PhysicsPlacementChanged -= RefreshPhysics;
            viewport.GlassShatterChanged -= RefreshPhysics;
        };
        if (openedForSimulation)
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
        menu.Items.Add(script);
        if (LightObjectMenu.Create(session, document, hits.FirstOrDefault().Item, inspectEntity, status) is { } lighting)
        {
            menu.Items.Add(lighting);
            menu.Items.Add(new Separator());
        }
        foreach (var (label, kind) in new[]
                 {
                     ("Structural", BrushKind.Structural), ("Detail", BrushKind.Detail),
                     ("Non Collide", BrushKind.NonColliding), ("Weapon Clip", BrushKind.WeaponClip),
                     ("Breakable glass", BrushKind.BreakableGlass)
                 })
        {
            var entry = new MenuItem { Header = label };
            entry.Click += (_, _) => { if (IsCurrent()) classify(kind); };
            kinds.Items.Add(entry);
        }
        kinds.Items.Add(waterItem);
        menu.Items.Add(kinds);
        menu.Items.Add(playerClip);
        menu.Items.Add(destructible);
        menu.Items.Add(physicsMenuSeparator);
        menu.Items.Add(physics);
        Refresh();
        menu.Open(viewport);
        return menu;

        bool IsCurrent() => ReferenceEquals(viewport.Session, session) &&
            ReferenceEquals(document, session.Document);

        void Refresh()
        {
            script.IsEnabled = IsCurrent() && session.Selection.Active is { } active && MapOrganization.Entity(document, active) is not null;
            foreach (var (item, entry) in entries)
            {
                entry.IsChecked = IsCurrent() && session.Selection.Contains(item);
                entry.IsEnabled = IsCurrent() && session.Visibility.CanSelect(document, item);
            }
            selectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && !entry.Menu.IsChecked);
            deselectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && entry.Menu.IsChecked);
            bool canWater = IsCurrent() && WaterEditing.GetBrushes(session).Length > 0;
            waterItem.IsEnabled = canWater;
            kinds.IsEnabled = IsCurrent() && (canWater || session.Selection.Items.Select(EditorSelection.Owner).OfType<MapBrush>()
                .Any(brush => session.Visibility.CanSelect(document, brush)));
            playerClip.IsVisible = playerClip.IsEnabled = IsCurrent() && PlayerClipEditing.CanGenerateFromModels(session);
            MapEntity? destructibleTarget = IsCurrent() ? DestructibleTarget() : null;
            destructible.IsVisible = destructible.IsEnabled = destructibleTarget is not null;
            destructibleReset.IsVisible = destructibleTarget is not null &&
                ReferenceEquals(destructibleTarget, session.Scene.DestructiblePreviewSource);
            RefreshPhysics();
        }

        MapEntity? DestructibleTarget()
        {
            MapEntity[] candidates = hits.Select(hit => session.Scene.Owner(hit.Item)).OfType<MapEntity>()
                .Where(entity => document.Entities.Contains(entity) && DestructiblePresets.HasDiscoveryName(entity) &&
                    session.Visibility.CanSelect(document, entity)).Distinct().ToArray();
            return candidates.FirstOrDefault(entity => ReferenceEquals(entity, session.Selection.Active)) ?? candidates.FirstOrDefault();
        }

        MapEntity? RuntimePhysicsTarget()
        {
            MapEntity[] candidates = hits.Select(hit => session.Scene.Owner(hit.Item)).OfType<MapEntity>()
                .Where(entity => document.Entities.Contains(entity) && entity.ClassName is ("misc_model" or "dyn_model") &&
                    session.Visibility.CanSelect(document, entity)).Distinct().ToArray();
            return candidates.FirstOrDefault(entity => ReferenceEquals(entity, session.Selection.Active)) ?? candidates.FirstOrDefault();
        }

        void SetRuntimePhysics(bool enabled)
        {
            if (!IsCurrent() || RuntimePhysicsTarget() is not { } entity) return;
            try
            {
                RuntimePhysicsEditing.Set(session, entity, enabled);
                status(enabled ? "Runtime physics enabled for the stock soccer_ball preset."
                    : "Runtime physics disabled; model restored to static misc_model.");
            }
            catch (ArgumentException exception) { status(exception.Message); }
        }

        void RefreshPhysics()
        {
            bool active = viewport.PhysicsPlacementActive;
            bool glassActive = viewport.GlassShatterActive;
            if (openedForSimulation && (!active && !glassActive || !IsCurrent()))
            {
                menu.Close();
                return;
            }
            bool canStart = CanStartPhysics();
            bool canShatter = CameraGlassShatterSimulation.CanStart(session);
            bool showPlacement = active || !glassActive && canStart;
            bool showShatter = glassActive || !active && canShatter;
            MapEntity? runtimeTarget = IsCurrent() && !openedForSimulation ? RuntimePhysicsTarget() : null;
            bool showRuntime = runtimeTarget is not null;
            enableRuntimePhysics.IsVisible = runtimeTarget?.ClassName == "misc_model";
            disableRuntimePhysics.IsVisible = runtimeTarget?.ClassName == "dyn_model";
            if (enableRuntimePhysics.IsVisible && runtimeTarget is not null)
            {
                enableRuntimePhysics.IsEnabled = RuntimePhysicsEditing.CanSet(session, runtimeTarget, true, out string reason);
                ToolTip.SetTip(enableRuntimePhysics, enableRuntimePhysics.IsEnabled
                    ? "Convert this model to the stock soccer_ball runtime physics preset." : reason);
            }
            disableRuntimePhysics.IsEnabled = runtimeTarget is not null &&
                RuntimePhysicsEditing.CanSet(session, runtimeTarget, false, out _);
            runtimePhysicsSeparator.IsVisible = showRuntime && (showPlacement || showShatter);
            physics.IsVisible = showRuntime || showPlacement || showShatter;
            physicsMenuSeparator.IsVisible = physics.IsVisible;
            physics.IsEnabled = IsCurrent();
            drop.Header = active && viewport.PhysicsPlacementRunning ? "Pause" :
                active && viewport.PhysicsPlacementHasStarted ? "Resume" : "Drop";
            drop.IsVisible = showPlacement;
            drop.IsEnabled = physics.IsEnabled && !glassActive && (active
                ? !viewport.PhysicsPlacementPreparing && !viewport.PhysicsPlacementNeedsReset &&
                  !viewport.PhysicsPlacementSettled
                : canStart);
            reset.IsVisible = keep.IsVisible = cancel.IsVisible = active;
            reset.IsEnabled = IsCurrent() && active && !viewport.PhysicsPlacementPreparing &&
                (viewport.PhysicsPlacementHasStarted || viewport.PhysicsPlacementNeedsReset);
            keep.IsEnabled = IsCurrent() && active && viewport.PhysicsPlacementHasChanges &&
                !viewport.PhysicsPlacementNeedsReset;
            cancel.IsEnabled = IsCurrent() && active;
            shatter.IsVisible = showShatter;
            shatter.IsEnabled = IsCurrent() && !active && !viewport.GlassShatterPreparing && canShatter;
            shatterStop.IsVisible = glassActive;
            shatterStop.IsEnabled = IsCurrent() && glassActive;
        }

        bool CanStartPhysics()
        {
            if (session.HasPlacement || viewport.FoliagePaintingEnabled || viewport.MistPaintingEnabled) return false;
            return CameraPrefabPlacementSimulation.CanStart(session) &&
                session.Selection.Items.OfType<MapEntity>().All(entity => entity.ClassName is "misc_model" or "script_model");
        }
    }
}
