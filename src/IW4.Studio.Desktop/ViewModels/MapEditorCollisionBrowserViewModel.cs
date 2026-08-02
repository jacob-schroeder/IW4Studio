using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

public enum MapEditorCollisionCategory
{
    StaticModels,
    BrushVolumes,
    TriangleGeometry
}

public sealed record MapEditorCollisionKindFilter(
    string Label,
    MapEditorCollisionCategory? Category);

public sealed record MapEditorCollisionBrowserGroup(
    MapEditorCollisionCategory Category,
    string Label,
    IReadOnlyList<EditorMapObject> Objects);

public enum MapEditorCollisionBrowserRowKind
{
    GroupHeader,
    Object
}

public sealed record MapEditorCollisionBrowserRow(
    MapEditorCollisionBrowserRowKind RowKind,
    string Label,
    string? GroupDetail,
    EditorMapObject? Object)
{
    public bool IsGroupHeader =>
        RowKind == MapEditorCollisionBrowserRowKind.GroupHeader;

    public bool IsObject =>
        RowKind == MapEditorCollisionBrowserRowKind.Object;

    public string Detail =>
        Object is null
            ? GroupDetail ?? string.Empty
            : MapEditorCollisionBrowserViewModel.Describe(Object);
}

/// <summary>
/// Read-only projection of every semantic collision representation. Browser
/// state is editor chrome: search, grouping, and selection never enter the
/// map command journal.
/// </summary>
public sealed class MapEditorCollisionBrowserViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly Action<EditorMapObject?> _selectObject;
    private string _searchText = string.Empty;
    private MapEditorCollisionKindFilter _selectedKindFilter;
    private IReadOnlyList<MapEditorCollisionBrowserGroup> _visibleGroups = [];
    private IReadOnlyList<MapEditorCollisionBrowserRow> _visibleRows = [];
    private MapEditorCollisionBrowserRow? _selectedRow;
    private MapObjectId? _selectedObjectId;
    private bool _isActivated;
    private bool _synchronizingSelection;

    public MapEditorCollisionBrowserViewModel(
        EditorMapDocument document,
        Action<EditorMapObject?> selectObject)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _selectObject = selectObject ??
            throw new ArgumentNullException(nameof(selectObject));
        KindFilters = Array.AsReadOnly(
        [
            new MapEditorCollisionKindFilter("All collision", null),
            new MapEditorCollisionKindFilter(
                "Static-model collision",
                MapEditorCollisionCategory.StaticModels),
            new MapEditorCollisionKindFilter(
                "Brush volumes",
                MapEditorCollisionCategory.BrushVolumes),
            new MapEditorCollisionKindFilter(
                "Triangle geometry",
                MapEditorCollisionCategory.TriangleGeometry)
        ]);
        _selectedKindFilter = KindFilters[0];
    }

    public IReadOnlyList<MapEditorCollisionKindFilter> KindFilters { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                Refresh();
        }
    }

    public MapEditorCollisionKindFilter SelectedKindFilter
    {
        get => _selectedKindFilter;
        set
        {
            if (value is not null &&
                SetProperty(ref _selectedKindFilter, value))
            {
                Refresh();
            }
        }
    }

    public IReadOnlyList<MapEditorCollisionBrowserGroup> VisibleGroups
    {
        get => _visibleGroups;
        private set => SetProperty(ref _visibleGroups, value);
    }

    public IReadOnlyList<MapEditorCollisionBrowserRow> VisibleRows
    {
        get => _visibleRows;
        private set
        {
            if (SetProperty(ref _visibleRows, value))
                OnPropertyChanged(nameof(ResultText));
        }
    }

    public MapEditorCollisionBrowserRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (value is { IsObject: false })
            {
                OnPropertyChanged();
                return;
            }
            if (!SetProperty(ref _selectedRow, value))
                return;

            if (!_synchronizingSelection)
            {
                _selectedObjectId = value?.Object?.Id;
                _selectObject(value?.Object);
            }
        }
    }

    public int TotalObjectCount =>
        EnumerateCollisionObjects().Count();

    public int VisibleObjectCount =>
        VisibleGroups.Sum(group => group.Objects.Count);

    public string ResultText =>
        IsActivated
            ? $"{VisibleObjectCount:N0} of {TotalObjectCount:N0} collision objects"
            : $"{TotalObjectCount:N0} collision objects";

    public bool IsActivated => _isActivated;

    public void Activate()
    {
        if (_isActivated)
            return;

        _isActivated = true;
        OnPropertyChanged(nameof(IsActivated));
        Refresh();
    }

    public void Refresh()
    {
        if (!IsActivated)
        {
            OnPropertyChanged(nameof(ResultText));
            return;
        }

        string search = SearchText.Trim();
        IEnumerable<EditorMapObject> objects =
            EnumerateCollisionObjects();
        if (SelectedKindFilter.Category is { } category)
        {
            objects = objects.Where(value =>
                GetCategory(value) == category);
        }
        if (search.Length != 0)
        {
            objects = objects.Where(value =>
                value.DisplayName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                value.Id.ToString().Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                Describe(value).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }

        EditorMapObject[] filtered = objects.ToArray();
        MapEditorCollisionBrowserGroup[] groups =
            Enum.GetValues<MapEditorCollisionCategory>()
                .Select(category => new MapEditorCollisionBrowserGroup(
                    category,
                    GetGroupLabel(category),
                    Array.AsReadOnly(
                        filtered
                            .Where(value =>
                                GetCategory(value) == category)
                            .OrderBy(GetSourceOrdinal)
                            .ThenBy(value =>
                                value.Id.Value.ToString("N"),
                                StringComparer.Ordinal)
                            .ToArray())))
                .Where(group => group.Objects.Count != 0)
                .ToArray();
        VisibleGroups = Array.AsReadOnly(groups);
        VisibleRows = Array.AsReadOnly(
            groups.SelectMany(CreateRows).ToArray());

        SynchronizeSelection(
            _selectedObjectId is { } id &&
            _document.TryGetObject(id, out EditorMapObject? selected)
                ? selected
                : null);
    }

    public void SynchronizeSelection(EditorMapObject? selected)
    {
        bool isCollision =
            selected is not null &&
            IsCollisionObject(selected);
        _selectedObjectId = isCollision
            ? selected!.Id
            : null;
        MapEditorCollisionBrowserRow? row = isCollision
                ? VisibleRows.FirstOrDefault(value =>
                    value.Object?.Id == selected!.Id)
                : null;
        _synchronizingSelection = true;
        try
        {
            SelectedRow = row;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    public static bool IsCollisionObject(EditorMapObject value) =>
        value is EditorCollisionObject or
            EditorAuthoredCollisionObject ||
        value is EditorStaticModel
        {
            Representation: StaticModelRepresentation.Collision
        };

    private IEnumerable<EditorMapObject> EnumerateCollisionObjects() =>
        _document.StaticModels
            .Where(value =>
                value.Representation ==
                StaticModelRepresentation.Collision)
            .Cast<EditorMapObject>()
            .Concat(_document.Collision)
            .Concat(_document.AuthoredCollision);

    private static IEnumerable<MapEditorCollisionBrowserRow> CreateRows(
        MapEditorCollisionBrowserGroup group)
    {
        yield return new MapEditorCollisionBrowserRow(
            MapEditorCollisionBrowserRowKind.GroupHeader,
            group.Label,
            $"{group.Objects.Count:N0}",
            Object: null);
        foreach (EditorMapObject value in group.Objects)
        {
            yield return new MapEditorCollisionBrowserRow(
                MapEditorCollisionBrowserRowKind.Object,
                value.DisplayName,
                GroupDetail: null,
                value);
        }
    }

    private static MapEditorCollisionCategory GetCategory(
        EditorMapObject value) =>
        value switch
        {
            EditorStaticModel
            {
                Representation: StaticModelRepresentation.Collision
            } => MapEditorCollisionCategory.StaticModels,
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Brush
            } => MapEditorCollisionCategory.BrushVolumes,
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Triangle
            } => MapEditorCollisionCategory.TriangleGeometry,
            EditorAuthoredCollisionObject
            {
                Source.GeometryKind:
                    CollisionGeometryKind.StaticModelHull
            } => MapEditorCollisionCategory.StaticModels,
            EditorAuthoredCollisionObject
            {
                Source.GeometryKind:
                    CollisionGeometryKind.ConvexBrush
            } => MapEditorCollisionCategory.BrushVolumes,
            EditorAuthoredCollisionObject
            {
                Source.GeometryKind:
                    CollisionGeometryKind.TriangleMesh
            } => MapEditorCollisionCategory.TriangleGeometry,
            _ => throw new ArgumentException(
                "The object is not a collision representation.",
                nameof(value))
        };

    private static string GetGroupLabel(
        MapEditorCollisionCategory category) =>
        category switch
        {
            MapEditorCollisionCategory.StaticModels =>
                "STATIC-MODEL COLLISION",
            MapEditorCollisionCategory.BrushVolumes =>
                "BRUSH VOLUMES",
            MapEditorCollisionCategory.TriangleGeometry =>
                "TRIANGLE GEOMETRY",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

    private static int GetSourceOrdinal(EditorMapObject value) =>
        value switch
        {
            EditorStaticModel model => model.SourceOrdinal.Value,
            EditorCollisionObject collision =>
                collision.SourceOrdinal.Value,
            EditorAuthoredCollisionObject => int.MaxValue,
            _ => int.MaxValue
        };

    internal static string Describe(EditorMapObject value) =>
        value switch
        {
            EditorStaticModel model =>
                $"Static model · row #{model.SourceOrdinal.Value} · " +
                (model.ModelName.Value ?? "(unresolved model)"),
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Brush
            } brush =>
                $"Brush volume · {brush.SupportingRecordCount.Value:N0} " +
                $"sides · {FormatContents(brush.Contents.Value)}",
            EditorCollisionObject triangle =>
                $"Triangle geometry · " +
                $"{triangle.SupportingRecordCount.Value:N0} vertices · " +
                FormatContents(triangle.Contents.Value),
            EditorAuthoredCollisionObject
            {
                Source: AuthoredConvexBrushCollisionSource brush
            } =>
                $"Authored brush · {brush.Faces.Count:N0} faces · " +
                $"contents 0x{brush.Contents:X8}",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredIndexedTriangleMeshCollisionSource mesh
            } =>
                $"Authored mesh · {mesh.Triangles.Count:N0} triangles · " +
                $"{mesh.Vertices.Count:N0} vertices",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredPairedStaticModelCollisionSource model
            } =>
                $"Authored static-model collision · " +
                model.ExactSerializedModelName,
            _ => value.Kind.ToString()
        };

    private static string FormatContents(uint? contents) =>
        contents is { } value
            ? $"contents 0x{value:X8}"
            : "contents unavailable";
}
