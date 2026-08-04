using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
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
        MenuItemPayloadValue payload) =>
        payload switch
        {
            MenuNoItemPayloadValue => new ItemPayloadBuildResult(
                new ItemDefData
                {
                    Value = new NoItemDefData
                    {
                        Reserved = source?.TypeData.Value is NoItemDefData existing
                            ? existing.Reserved
                            : 0
                    }
                }),
            MenuEditFieldPayloadValue edit => new ItemPayloadBuildResult(
                new ItemDefData
                {
                    Value = new EditFieldItemDefData
                    {
                        EditFieldPointer = source?.TypeData.Value is EditFieldItemDefData existing
                            ? existing.EditFieldPointer
                            : new XPointer<EditFieldDef>(-1)
                    }
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
                new ItemDefData
                {
                    Value = new DvarEnumItemDefData
                    {
                        DvarEnumNamePointer = StringPointer(
                            source?.TypeData.Value is DvarEnumItemDefData existing
                                ? existing.DvarEnumNamePointer
                                : default,
                            dvar.DvarName)
                    }
                },
                DvarEnumName: dvar.DvarName),
            MenuNewsTickerPayloadValue ticker => new ItemPayloadBuildResult(
                new ItemDefData
                {
                    Value = new NewsTickerItemDefData
                    {
                        NewsTickerPointer = source?.TypeData.Value is NewsTickerItemDefData existing
                            ? existing.NewsTickerPointer
                            : new XPointer<NewsTickerDef>(-1)
                    }
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
                new ItemDefData
                {
                    Value = new TextScrollItemDefData
                    {
                        TextScrollPointer = source?.TypeData.Value is TextScrollItemDefData existing
                            ? existing.TextScrollPointer
                            : new XPointer<TextScrollDef>(-1)
                    }
                },
                TextScroll: new TextScrollDef { StartTime = 0 }),
            _ => throw new InvalidDataException(
                $"Unsupported Menu editor payload '{payload.GetType().Name}'.")
        };

    private static ItemPayloadBuildResult BuildListBox(
        ItemDefAsset? source,
        MenuListBoxPayloadValue value)
    {
        ListBoxDef? existing = source?.ListBox;
        return new ItemPayloadBuildResult(
            new ItemDefData
            {
                Value = new ListBoxItemDefData
                {
                    ListBoxPointer = source?.TypeData.Value is ListBoxItemDefData data
                        ? data.ListBoxPointer
                        : new XPointer<ListBoxDef>(-1)
                }
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
            new ItemDefData
            {
                Value = new MultiItemDefData
                {
                    MultiPointer = source?.TypeData.Value is MultiItemDefData data
                        ? data.MultiPointer
                        : new XPointer<MultiDef>(-1)
                }
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
