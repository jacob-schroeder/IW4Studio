using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

internal static partial class MenuDocumentCompiler
{
    private static XPointer<string> PointerAt(
        IReadOnlyList<XPointer<string>>? pointers,
        int index) => pointers is not null && index < pointers.Count
        ? pointers[index]
        : default;

    private static XPointer<string> StringPointer(
        XPointer<string> source,
        string? value) => value is null ? default : source;

    private static XPointer<T> ReferencePointer<T>(
        XPointer<T> source,
        string? value) => value is null ? default : source;

    private static IReadOnlyList<ItemDefReference> Reindex(
        IReadOnlyList<ItemDefReference> values) =>
        values.Select((value, index) => new ItemDefReference(
                index,
                value.Pointer,
                value.Item))
            .ToArray();

    private static int ItemIndex(
        MenuDocumentIdentity identity,
        MenuNodeId id)
    {
        for (int index = 0; index < identity.Items.Count; index++)
        {
            if (identity.Items[index].Id == id)
                return index;
        }

        throw new KeyNotFoundException($"Menu item '{id}' is not present in this draft.");
    }

    private static ItemDefAsset RequireItem(MenuDefAsset definition, int index) =>
        definition.Items[index].Item
        ?? throw new InvalidOperationException(
            $"Menu item {index} is unresolved and cannot be edited.");

    private static int InsertIndex(int? requested, int count)
    {
        int index = requested ?? count;
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(nameof(requested));
        return index;
    }

    private static int ExistingDestination(int requested, int count)
    {
        if (requested < 0 || requested >= count)
            throw new ArgumentOutOfRangeException(nameof(requested));
        return requested;
    }

    private static void RequireLockedRootName(string? expected, string? proposed)
    {
        if (!string.Equals(expected, proposed, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The root Window name is the Menu asset identity and cannot be changed by a property edit.");
    }

    private static RectangleDef? RectangleAt(
        IReadOnlyList<RectangleDef>? values,
        int index) =>
        values is not null && (uint)index < (uint)values.Count
            ? values[index]
            : null;

    private static RectangleDef Rect(
        RectangleDef? source,
        MenuRectangleValue value) => new()
    {
        X = value.X,
        Y = value.Y,
        W = value.Width,
        H = value.Height,
        HorzAlign = value.HorizontalAlignment,
        VertAlign = value.VerticalAlignment,
        Pad12 = source?.Pad12 ?? 0
    };

    private static Vec4 Vec(MenuColorValue value) => new()
    {
        // Window color Vec4 slots are serialized R/G/B/A despite the
        // generic asset container's historical A/R/G/B property names.
        A = value.R,
        R = value.G,
        G = value.B,
        B = value.A
    };

    private static Vec4 Copy(Vec4 value) => new()
    {
        A = value.A,
        R = value.R,
        G = value.G,
        B = value.B
    };

    private static IReadOnlyList<MenuTransition> Clone(
        IReadOnlyList<MenuTransition> values) =>
        values.Select(Clone).ToArray();

    private static MenuTransition Clone(MenuTransition value) => new()
    {
        TransitionType = value.TransitionType,
        TargetField = value.TargetField,
        StartTime = value.StartTime,
        StartValue = value.StartValue,
        EndValue = value.EndValue,
        Time = value.Time,
        EndTriggerType = value.EndTriggerType
    };

    private static IReadOnlyList<MenuTransition> Transitions(
        IReadOnlyList<MenuTransitionValue> values) =>
        values.Select(value => new MenuTransition
        {
            TransitionType = value.TransitionType,
            TargetField = value.TargetField,
            StartTime = value.StartTime,
            StartValue = value.StartValue,
            EndValue = value.EndValue,
            Time = value.Time,
            EndTriggerType = value.EndTriggerType
        }).ToArray();

    private static string? LogicalReferenceName(string? value) =>
        string.IsNullOrEmpty(value) ? value : value.TrimStart(',');

    internal sealed record MenuEditResult(
        MenuBuildData Data,
        MenuDocumentIdentity Identity);

    private sealed record ItemPayloadBuildResult(
        ItemDefData TypeData,
        EditFieldDef? EditField = null,
        ListBoxDef? ListBox = null,
        MultiDef? Multi = null,
        string? DvarEnumName = null,
        NewsTickerDef? NewsTicker = null,
        TextScrollDef? TextScroll = null);
}
