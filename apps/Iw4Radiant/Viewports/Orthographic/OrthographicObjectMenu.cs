using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Orthographic;

internal static class OrthographicObjectMenu
{
    internal static ContextMenu Open(Control viewport, EditorSession session, object? hit, Vector3 position,
        Action<BrushKind> classify, Action<MapEntity> inspectEntity, Action showModels, Action showPrefabs,
        Action showOrganization, Action<string> status)
    {
        MapDocument document = session.Document;
        var menu = new ContextMenu();
        if (hit is not null)
        {
            bool selected = session.Selection.Contains(hit);
            var select = new MenuItem { Header = (selected ? "Deselect " : "Select ") + Label(session, hit) };
            select.Click += (_, _) =>
            {
                if (IsCurrent()) session.Select(hit, additive: true, toggle: true);
            };
            menu.Items.Add(select);
            menu.Items.Add(new Separator());
        }

        var duplicate = new MenuItem { Header = "Duplicate selection", IsEnabled = CanEditSelection(session) };
        duplicate.Click += (_, _) => Run(() => SelectionEditing.Duplicate(session), "Duplicated selection.");
        menu.Items.Add(duplicate);

        var delete = new MenuItem { Header = "Delete selection", IsEnabled = CanEditSelection(session) };
        delete.Click += (_, _) => Run(() => SelectionEditing.Delete(session), "Deleted selection.");
        menu.Items.Add(delete);

        MapEntity? currentEntity = session.Selection.Active is { } active
            ? MapOrganization.Entity(document, active) : null;
        if (currentEntity is { ClassName: not "worldspawn" })
        {
            var properties = new MenuItem { Header = $"Edit {currentEntity.ClassName} properties" };
            properties.Click += (_, _) =>
            {
                if (!IsCurrent()) return;
                session.Select(currentEntity);
                inspectEntity(currentEntity);
            };
            menu.Items.Add(properties);
        }

        if (session.Selection.Active is MapEntity { ClassName: "func_group" } group)
        {
            var ungroup = new MenuItem { Header = "Ungroup entity" };
            ungroup.Click += (_, _) => Run(() => session.Edit(() =>
                session.Selection.SetRange(MapOrganization.Ungroup(document, group))), "Ungrouped entity.");
            menu.Items.Add(ungroup);
        }

        if (session.Selection.Active is MapEntity { ClassName: "misc_prefab" } prefab)
        {
            var explode = new MenuItem { Header = "Explode prefab" };
            explode.Click += (_, _) => Run(() => session.Prefabs.Explode(session, prefab), "Exploded prefab.");
            menu.Items.Add(explode);
        }

        var layers = new MenuItem { Header = "Assign to layer", IsEnabled = CanEditSelection(session) };
        foreach (string layer in MapOrganization.Layers(document))
        {
            var assign = new MenuItem { Header = layer };
            assign.Click += (_, _) => Run(() => session.Edit(() =>
            {
                foreach (object item in session.Selection.Items) MapOrganization.Assign(item, layer);
                session.Visibility.Invalidate();
                session.Selection.SetRange(session.Selection.Items.Where(item => session.Visibility.CanSelect(document, item)));
            }), $"Assigned selection to {layer}.");
            layers.Items.Add(assign);
        }
        menu.Items.Add(layers);
        var organization = new MenuItem { Header = "Layers, groups and visibility…" };
        organization.Click += (_, _) => { if (IsCurrent()) showOrganization(); };
        menu.Items.Add(organization);

        menu.Items.Add(new Separator());
        MenuItem brushTypes = BrushTypes(session, document, classify);
        menu.Items.Add(brushTypes);
        var geometry = session.Selection.Items.Select(EditorSelection.Owner)
            .Where(item => item is MapBrush or MapTerrain && session.Visibility.CanSelect(document, item))
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
        var tools = new MenuItem { Header = "Brush / mesh tools", IsEnabled = geometry.Length > 0 };
        foreach (bool split in new[] { true, false })
        {
            var item = new MenuItem { Header = split ? "Split Coplanar Geo" : "Don't Split Coplanar Geo" };
            item.Click += (_, _) => Run(() => session.Edit(() =>
            {
                foreach (object item in geometry)
                    MapToolFlags.SetSplitCoplanar(item is MapBrush brush ? brush.Directives : ((MapTerrain)item).Directives, split);
            }), split ? "Set Split Coplanar Geo." : "Cleared Split Coplanar Geo.");
            tools.Items.Add(item);
        }
        menu.Items.Add(tools);
        menu.Items.Add(new Separator());
        menu.Items.Add(EntityTypes(session, position, inspectEntity, showModels, showPrefabs, status, IsCurrent));
        menu.Open(viewport);
        return menu;

        bool IsCurrent() => ReferenceEquals(document, session.Document);

        void Run(Action action, string message)
        {
            if (!IsCurrent()) return;
            try { action(); status(message); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            { status(exception.Message); }
        }
    }

    private static MenuItem BrushTypes(EditorSession session, MapDocument document, Action<BrushKind> classify)
    {
        var menu = new MenuItem
        {
            Header = "Brush type",
            IsEnabled = session.Selection.Items.Select(EditorSelection.Owner).OfType<MapBrush>()
                .Any(brush => session.Visibility.CanSelect(document, brush))
        };
        foreach (var (label, kind) in new[]
                 {
                     ("Structural", BrushKind.Structural), ("Detail", BrushKind.Detail),
                     ("Non Collide", BrushKind.NonColliding), ("Weapon Clip", BrushKind.WeaponClip)
                 })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => classify(kind);
            menu.Items.Add(item);
        }
        return menu;
    }

    private static MenuItem EntityTypes(EditorSession session, Vector3 position, Action<MapEntity> inspectEntity,
        Action showModels, Action showPrefabs, Action<string> status,
        Func<bool> isCurrent)
    {
        var create = new MenuItem { Header = "Create entity" };
        foreach (var category in GameplayEntityEditing.Types.GroupBy(type => type.Family))
        {
            var family = new MenuItem { Header = category.Key };
            var subfamilies = new Dictionary<string, MenuItem>(StringComparer.Ordinal);
            foreach (GameplayEntityType type in category)
            {
                MenuItem parent = family;
                if (category.Key == "mp")
                {
                    string prefix = type.Name.Split('_')[1];
                    if (!subfamilies.TryGetValue(prefix, out MenuItem? child))
                    {
                        child = new MenuItem { Header = prefix };
                        subfamilies.Add(prefix, child);
                        family.Items.Add(child);
                    }
                    parent = child;
                }
                var item = new MenuItem
                {
                    Header = type.Name,
                    IsEnabled = GameplayEntityEditing.CanCreate(session, type)
                };
                item.Click += (_, _) =>
                {
                    if (!isCurrent()) return;
                    try
                    {
                        switch (type.Creation)
                        {
                            case GameplayEntityCreation.Brush:
                                inspectEntity(GameplayEntityEditing.CreateBrushEntity(session, type.Name));
                                status($"Created {type.Name}.");
                                break;
                            case GameplayEntityCreation.ConvertModels:
                                int count = GameplayEntityEditing.ConvertModels(session);
                                if (session.Selection.Active is MapEntity model) inspectEntity(model);
                                status($"Converted {count} model(s) to {type.Name}.");
                                break;
                            case GameplayEntityCreation.ModelBrowser:
                                showModels();
                                status("Choose an XModel, select Place, then click a grid or camera surface.");
                                break;
                            case GameplayEntityCreation.PrefabBrowser:
                                showPrefabs();
                                status("Choose a prefab, select Place, then click a grid or camera surface.");
                                break;
                            case GameplayEntityCreation.Group:
                                GameplayEntityEditing.Group(session);
                                if (session.Selection.Active is MapEntity group) inspectEntity(group);
                                status("Grouped selection.");
                                break;
                            default:
                                inspectEntity(GameplayEntityEditing.Place(session, type.Name, position));
                                status($"Created {type.Name}.");
                                break;
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
                    { status(exception.Message); }
                };
                parent.Items.Add(item);
            }
            create.Items.Add(family);
        }
        return create;
    }

    private static bool CanEditSelection(EditorSession session) => session.Selection.Count > 0 &&
        session.Selection.Items.All(item => item is not (BrushFaceSelection or BrushVertexSelection or TerrainVertexSelection) &&
            SelectionGeometry.CanTransform(item) && session.Visibility.CanSelect(session.Document, item));

    private static string Label(EditorSession session, object item) => EditorSelection.Owner(item) switch
    {
        MapEntity entity => entity.ClassName,
        MapTerrain terrain => terrain.IsCurve ? "curve" : "terrain",
        MapBrush brush when MapOrganization.Entity(session.Document, brush) is { ClassName: not "worldspawn" } entity =>
            entity.ClassName + " brush",
        MapBrush => "world brush",
        _ => "object"
    };
}
