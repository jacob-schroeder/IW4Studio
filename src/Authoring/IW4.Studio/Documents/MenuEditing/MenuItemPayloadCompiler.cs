using IW4.Game.Assets.Menu;
using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

internal static partial class MenuDocumentCompiler
{
    private static ItemPayloadBuildResult PreservePayload(
        ItemDefAsset? source)
    {
        if (source is null)
        {
            throw new InvalidOperationException(
                "A new Menu item cannot preserve an existing payload.");
        }

        return new ItemPayloadBuildResult(
            source.TypeData,
            source.EditField,
            source.ListBox,
            source.Multi,
            source.DvarEnumName,
            source.NewsTicker,
            source.TextScroll);
    }

    private static ItemPayloadBuildResult BuildPayload(
        ItemDefAsset? source,
        ItemDefType type,
        MenuItemPayloadValue payload) =>
        payload switch
        {
            MenuNoItemPayloadValue => BuildNullPayload(source, type),
            MenuEditFieldPayloadValue edit => new ItemPayloadBuildResult(
                new EditFieldItemDefData
                {
                    EditFieldPointer = source is { TypeData: EditFieldItemDefData existing }
                        ? existing.EditFieldPointer
                        : new XPointer<EditFieldDef>(-1)
                },
                EditField: new EditFieldDef
                {
                    MinVal = edit.MinValue,
                    MaxVal = edit.MaxValue,
                    DefVal = edit.DefaultValue,
                    Range = edit.Range,
                    MaxChars = edit.MaxChars,
                    MaxCharsGotoNext = edit.MaxCharsGotoNext,
                    MaxPaintChars = edit.MaxPaintChars,
                    PaintOffset = edit.PaintOffset
                }),
            MenuListBoxPayloadValue list => BuildListBox(source, list),
            MenuMultiPayloadValue multi => BuildMulti(source, multi),
            MenuDvarEnumPayloadValue dvar => new ItemPayloadBuildResult(
                new DvarEnumItemDefData
                {
                    DvarEnumNamePointer = StringPointer(
                        source is { TypeData: DvarEnumItemDefData existing }
                            ? existing.DvarEnumNamePointer
                            : default,
                        dvar.DvarName)
                },
                DvarEnumName: dvar.DvarName),
            MenuNewsTickerPayloadValue ticker => new ItemPayloadBuildResult(
                new NewsTickerItemDefData
                {
                    NewsTickerPointer = source is { TypeData: NewsTickerItemDefData existing }
                        ? existing.NewsTickerPointer
                        : new XPointer<NewsTickerDef>(-1)
                },
                NewsTicker: new NewsTickerDef
                {
                    FeedId = ticker.FeedId,
                    Speed = ticker.Speed,
                    Spacing = ticker.Spacing,
                    LastTime = 0,
                    Start = 0,
                    End = 0,
                    X = ticker.X
                }),
            MenuTextScrollPayloadValue => new ItemPayloadBuildResult(
                new TextScrollItemDefData
                {
                    TextScrollPointer = source is { TypeData: TextScrollItemDefData existing }
                        ? existing.TextScrollPointer
                        : new XPointer<TextScrollDef>(-1)
                },
                TextScroll: new TextScrollDef { StartTime = 0 }),
            _ => throw new InvalidDataException(
                $"Unsupported Menu editor payload '{payload.GetType().Name}'.")
        };

    private static ItemPayloadBuildResult BuildNullPayload(
        ItemDefAsset? source,
        ItemDefType type)
    {
        ItemDefData value = type switch
        {
            ItemDefType.Text
                or ItemDefType.EditField
                or ItemDefType.NumericField
                or ItemDefType.Slider
                or ItemDefType.YesNo
                or ItemDefType.Bind
                or ItemDefType.Validation
                or ItemDefType.DecimalField
                or ItemDefType.UpDown
                or ItemDefType.EmailField
                or ItemDefType.PassWordField => new EditFieldItemDefData(),
            ItemDefType.ListBox => new ListBoxItemDefData(),
            ItemDefType.Multi => new MultiItemDefData(),
            ItemDefType.DvarEnum => new DvarEnumItemDefData(),
            ItemDefType.NewsTicker => new NewsTickerItemDefData(),
            ItemDefType.TextScroll => new TextScrollItemDefData(),
            _ when Enum.IsDefined(type) => new NoItemDefData
            {
                Reserved = source is { TypeData: NoItemDefData existing }
                    ? existing.Reserved
                    : 0
            },
            _ => throw new InvalidDataException(
                $"Unsupported Menu item type '{type}'.")
        };

        return new ItemPayloadBuildResult(value);
    }

    private static ItemPayloadBuildResult BuildListBox(
        ItemDefAsset? source,
        MenuListBoxPayloadValue value)
    {
        ListBoxDef? existing = source?.ListBox;
        return new ItemPayloadBuildResult(
            new ListBoxItemDefData
            {
                ListBoxPointer = source is { TypeData: ListBoxItemDefData data }
                    ? data.ListBoxPointer
                    : new XPointer<ListBoxDef>(-1)
            },
            ListBox: new ListBoxDef
            {
                StartPos = new int[4],
                EndPos = new int[4],
                DrawPadding = value.DrawPadding,
                ElementWidth = value.ElementWidth,
                ElementHeight = value.ElementHeight,
                ElementStyle = value.ElementStyle,
                NumColumns = value.NumColumns,
                ColumnInfo = value.Columns.Select(column => new ColumnInfo
                {
                    Pos = column.Position,
                    Width = column.Width,
                    MaxChars = column.MaxChars,
                    Alignment = column.Alignment
                }).ToArray(),
                DoubleClick = existing?.DoubleClick ?? default,
                DoubleClickSet = existing?.DoubleClickSet,
                NotSelectable = value.NotSelectable ? 1 : 0,
                NoScrollbars = value.NoScrollbars ? 1 : 0,
                UsePaging = value.UsePaging,
                SelectBorder = Vec(value.SelectBorder),
                SelectIcon = ReferencePointer(
                    existing?.SelectIcon ?? default,
                    value.SelectIconMaterialName),
                SelectIconMaterialName = LogicalReferenceName(value.SelectIconMaterialName)
            });
    }

    private static ItemPayloadBuildResult BuildMulti(
        ItemDefAsset? source,
        MenuMultiPayloadValue value)
    {
        MultiDef? existing = source?.Multi;
        return new ItemPayloadBuildResult(
            new MultiItemDefData
            {
                MultiPointer = source is { TypeData: MultiItemDefData data }
                    ? data.MultiPointer
                    : new XPointer<MultiDef>(-1)
            },
            Multi: new MultiDef
            {
                DvarList = value.Entries.Select((entry, index) =>
                        StringPointer(
                            PointerAt(existing?.DvarList, index),
                            entry.DvarListValue))
                    .ToArray(),
                DvarListStrings = value.Entries.Select(entry => entry.DvarListValue).ToArray(),
                DvarStr = value.Entries.Select((entry, index) =>
                        StringPointer(
                            PointerAt(existing?.DvarStr, index),
                            entry.DvarStringValue))
                    .ToArray(),
                DvarStrStrings = value.Entries.Select(entry => entry.DvarStringValue).ToArray(),
                DvarValue = value.Entries.Select(entry => entry.NumericValue).ToArray(),
                Count = value.Count,
                StrDef = value.StringDefinition
            });
    }
}
