using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports;

internal static class LightObjectMenu
{
    internal static MenuItem? Create(EditorSession session, MapDocument document, object? hit,
        Action<MapEntity> inspectEntity, Action<string> status)
    {
        MapEntity? entity = hit is MapEntity { ClassName: "light" } light ? light :
            hit is null && session.Selection.Active is MapEntity { ClassName: "light" } selected ? selected : null;
        if (entity is null || !document.Entities.Contains(entity)) return null;

        var lighting = new MenuItem { Header = "Lighting" };
        var omni = new MenuItem { Header = "Primary Omni", ToggleType = MenuItemToggleType.CheckBox };
        var spot = new MenuItem { Header = "Primary Spot", ToggleType = MenuItemToggleType.CheckBox };
        var shadows = new MenuItem { Header = "Dynamic Shadows", ToggleType = MenuItemToggleType.CheckBox };
        var sweep = new MenuItem { Header = "Sweep", ToggleType = MenuItemToggleType.CheckBox };
        var propertiesItem = new MenuItem { Header = "Properties" };
        BindToggle(omni, ChangeOmni);
        BindToggle(spot, ChangeSpot);
        BindToggle(shadows, properties => properties with
        {
            DynamicShadows = !properties.DynamicShadows
        });
        BindToggle(sweep, properties => properties with
        {
            SweepAngle = properties.SweepAngle > 0 ? 0 : 50,
            SweepSeconds = properties.SweepAngle > 0 ? properties.SweepSeconds : 3
        });
        ToolTip.SetShowOnDisabled(shadows, true);
        ToolTip.SetShowOnDisabled(sweep, true);
        propertiesItem.Click += (_, _) =>
        {
            if (!IsCurrent()) return;
            session.Select(entity);
            inspectEntity(entity);
        };
        lighting.Items.Add(omni);
        lighting.Items.Add(spot);
        lighting.Items.Add(shadows);
        lighting.Items.Add(sweep);
        lighting.Items.Add(new Separator());
        lighting.Items.Add(propertiesItem);
        Refresh();
        return lighting;

        bool IsCurrent() => ReferenceEquals(session.Document, document) &&
            document.Entities.Contains(entity) && session.Visibility.CanSelect(document, entity);

        void BindToggle(MenuItem item, Func<MapLightProperties, MapLightProperties> change)
        {
            item.Click += (_, _) =>
            {
                if (!IsCurrent()) return;
                if (!MapLightProperties.TryRead(entity, out MapLightProperties current, out string? error))
                {
                    status(error ?? "Light properties are invalid.");
                    return;
                }
                try
                {
                    MapLightProperties next = change(current);
                    MapEntity proposed = entity.Clone();
                    next.ApplyTo(proposed);
                    if (next.SweepAngle > 0 && current.SweepAngle == 0 &&
                        (!MapLight.TryCreate(proposed, session.Scene.ResolveTargets(proposed), out MapLight resolved,
                            out string? sweepError) || !resolved.IsMoving))
                        throw new ArgumentException(sweepError ??
                            "Sweep needs a resolved light with positive radius, intensity, and color.");
                    session.StopLightSweepPreview();
                    session.Edit(() => next.ApplyTo(entity));
                    status($"Updated light: {item.Header}.");
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
                { status(exception.Message); }
                Refresh();
            };
        }

        static MapLightProperties ChangeOmni(MapLightProperties properties)
        {
            bool enable = (properties.SpawnFlags & MapLightDefaults.PrimaryOmni) == 0;
            int flags = properties.SpawnFlags & ~(MapLightDefaults.PrimaryOmni | MapLightDefaults.PrimarySpot);
            if (enable) flags |= MapLightDefaults.PrimaryOmni;
            return properties with
            {
                SpawnFlags = flags,
                DynamicShadows = enable && properties.DynamicShadows,
                SweepAngle = 0
            };
        }

        static MapLightProperties ChangeSpot(MapLightProperties properties)
        {
            bool enable = (properties.SpawnFlags & MapLightDefaults.PrimarySpot) == 0;
            int flags = properties.SpawnFlags & ~(MapLightDefaults.PrimaryOmni | MapLightDefaults.PrimarySpot);
            if (enable) flags |= MapLightDefaults.PrimarySpot;
            return properties with
            {
                SpawnFlags = flags,
                DynamicShadows = enable && properties.DynamicShadows,
                SweepAngle = enable ? properties.SweepAngle : 0
            };
        }

        void Refresh()
        {
            bool current = IsCurrent();
            MapLightProperties properties = default;
            string? error = null;
            bool valid = current && MapLightProperties.TryRead(entity, out properties, out error);
            lighting.IsEnabled = current;
            propertiesItem.IsEnabled = current;
            omni.IsEnabled = spot.IsEnabled = valid;
            omni.IsChecked = valid && (properties.SpawnFlags & MapLightDefaults.PrimaryOmni) != 0;
            spot.IsChecked = valid && (properties.SpawnFlags & MapLightDefaults.PrimarySpot) != 0;
            bool primarySpot = valid && properties.SpawnFlags == MapLightDefaults.PrimarySpot;
            bool primaryOmni = valid && properties.SpawnFlags == MapLightDefaults.PrimaryOmni;
            shadows.IsEnabled = primaryOmni || primarySpot;
            sweep.IsEnabled = primarySpot;
            shadows.IsChecked = valid && properties.DynamicShadows;
            sweep.IsChecked = valid && properties.SweepAngle > 0;
            string? spotReason = valid ? "Requires a Primary Spot light." : error;
            ToolTip.SetTip(shadows, primaryOmni
                ? "Primary omni shadows project downward within this profile's fixed 120-degree cone and fade toward the edge. The light still shines in all directions."
                : primarySpot ? null : valid ? "Requires a Primary Omni or Primary Spot light." : error);
            ToolTip.SetTip(sweep, primarySpot ? null : spotReason);
            ToolTip.SetTip(omni, valid ? null : error);
            ToolTip.SetTip(spot, valid ? null : error);
        }
    }
}
