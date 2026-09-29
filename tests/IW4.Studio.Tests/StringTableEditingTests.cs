using IW4.Formats.SourceFormat.StringTable;
using IW4.Game.Assets.StringTable;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class StringTableEditingTests
{
    [Fact]
    public void AddColumn_makes_an_empty_table_editable()
    {
        StringTableDraft draft = CreateDraft(0, 0);

        draft.AddColumn();

        Assert.Equal(1, draft.RowCount);
        Assert.Equal(1, draft.ColumnCount);
        Assert.Equal(new StringTableCellDraft(string.Empty, 0), Assert.Single(draft.Cells));
    }

    [Fact]
    public void InsertRow_makes_an_empty_table_editable()
    {
        StringTableDraft draft = CreateDraft(0, 0);

        draft.InsertRow(0);

        Assert.Equal(1, draft.RowCount);
        Assert.Equal(1, draft.ColumnCount);
        Assert.Equal(new StringTableCellDraft(string.Empty, 0), Assert.Single(draft.Cells));
    }

    [Fact]
    public void InsertRow_preserves_existing_cells_and_hashes_in_row_order()
    {
        StringTableDraft draft = CreateDraft(2, 2,
            ("a", 11), ("b", 12), ("c", 21), ("d", 22));

        draft.InsertRow(1);

        Assert.Equal(3, draft.RowCount);
        Assert.Equal(2, draft.ColumnCount);
        Assert.Equal(
            [new("a", 11), new("b", 12), new(string.Empty, 0),
             new(string.Empty, 0), new("c", 21), new("d", 22)],
            draft.Cells);
    }

    [Fact]
    public void AddColumn_preserves_existing_cells_and_hashes_in_row_order()
    {
        StringTableDraft draft = CreateDraft(2, 2,
            ("a", 11), ("b", 12), ("c", 21), ("d", 22));

        draft.AddColumn();

        Assert.Equal(2, draft.RowCount);
        Assert.Equal(3, draft.ColumnCount);
        Assert.Equal(
            [new("a", 11), new("b", 12), new(string.Empty, 0),
             new("c", 21), new("d", 22), new(string.Empty, 0)],
            draft.Cells);
    }

    [Fact]
    public void Invalid_row_and_source_leave_destination_unchanged()
    {
        StringTableDraft draft = CreateDraft(1, 1, ("original", 42));
        StringTableDraft malformed = CreateDraft(1, 2, ("incomplete", 7));

        Assert.Throws<ArgumentOutOfRangeException>(() => draft.InsertRow(2));
        Assert.Throws<ArgumentException>(() => draft.ReplaceWith(malformed));

        Assert.Equal("test/table", draft.Name);
        Assert.Equal(1, draft.RowCount);
        Assert.Equal(1, draft.ColumnCount);
        Assert.Equal(new StringTableCellDraft("original", 42), Assert.Single(draft.Cells));
    }

    [Fact]
    public void ReplaceWith_publishes_cells_and_dimensions_without_changing_name()
    {
        StringTableDraft draft = CreateDraft(1, 1, ("old", 1));
        StringTableDraft source = new(new StringTableAsset
        {
            Name = "another/table",
            RowCount = 1,
            ColumnCount = 2,
            Cells = [new() { String = "first", Hash = 17 }, new() { String = "second", Hash = 19 }]
        });

        draft.ReplaceWith(source);

        Assert.Equal("test/table", draft.Name);
        Assert.Equal(1, draft.RowCount);
        Assert.Equal(2, draft.ColumnCount);
        Assert.Equal([new("first", 17), new("second", 19)], draft.Cells);
    }

    [Fact]
    public void Csv_import_preserves_records_quotes_whitespace_and_trailing_empty_cells()
    {
        const string csv = "first,\"a,b\",\"say \"\"hi\"\"\"\r\n" +
            "\"multi\r\nline\",,last\r\n" +
            "\r\n" +
            "short\n" +
            "x,,\r" +
            "  café  ,";

        StringTableAsset asset = new StringTableExchange().ReadCsv(
            new StringReader(csv), "imported/table");

        Assert.Equal("imported/table", asset.Name);
        Assert.Equal(6, asset.RowCount);
        Assert.Equal(3, asset.ColumnCount);
        Assert.Equal(
            ["first", "a,b", "say \"hi\"",
             "multi\r\nline", "", "last",
             "", "", "",
             "short", "", "",
             "x", "", "",
             "  café  ", "", ""],
            asset.Cells.Select(cell => cell.String));
        Assert.All(asset.Cells, cell => Assert.Equal(0, cell.Hash));
    }

    [Theory]
    [InlineData("\"unterminated", "unterminated")]
    [InlineData("ab\"cd", "unquoted")]
    [InlineData("\"a\"x", "closing quote")]
    [InlineData("a\0b", "embedded null")]
    public void Csv_import_rejects_malformed_input_with_location(string csv, string reason)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new StringTableExchange().ReadCsv(new StringReader(csv), "test/table"));

        Assert.Contains("record 1, line 1", exception.Message);
        Assert.Contains(reason, exception.Message);
    }

    [Fact]
    public void Csv_import_rejects_an_empty_file()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new StringTableExchange().ReadCsv(new StringReader(string.Empty), "test/table"));

        Assert.Equal("CSV file contains no rows.", exception.Message);
    }

    private static StringTableDraft CreateDraft(
        int rows,
        int columns,
        params (string Value, int Hash)[] cells) =>
        new(new StringTableAsset
        {
            Name = "test/table",
            RowCount = rows,
            ColumnCount = columns,
            Cells = cells.Select(cell => new StringTableCell
            {
                String = cell.Value,
                Hash = cell.Hash
            }).ToArray()
        });
}
