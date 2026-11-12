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
    public void Editing_replaces_a_stale_hash_without_changing_other_cells_or_source()
    {
        StringTableCell[] sourceCells =
        [
            new() { String = "old", Hash = 123 },
            new() { String = "untouched", Hash = 456 }
        ];
        StringTableDraft draft = new(new StringTableAsset
        {
            Name = "test/table",
            RowCount = 1,
            ColumnCount = 2,
            Cells = sourceCells
        });

        draft.SetCellValue(0, 0, "loadoutName");

        Assert.Equal(new StringTableCellDraft("loadoutName", unchecked((int)0x3c47df33)), draft.Cells[0]);
        Assert.Equal(new StringTableCellDraft("untouched", 456), draft.Cells[1]);
        Assert.Equal("old", sourceCells[0].String);
        Assert.Equal(123, sourceCells[0].Hash);
    }

    [Fact]
    public void Inserted_row_and_column_hash_new_values_without_rehashing_existing_cells()
    {
        StringTableDraft draft = CreateDraft(1, 1, ("existing", 777));

        draft.InsertRow(1);
        draft.AddColumn();
        draft.SetCellValue(1, 0, "loadoutPrimary");
        draft.SetCellValue(0, 1, "loadoutSecondary");

        Assert.Equal(2, draft.RowCount);
        Assert.Equal(2, draft.ColumnCount);
        Assert.Equal(
            [new("existing", 777), new("loadoutSecondary", unchecked((int)0xc2fcb5cc)),
             new("loadoutPrimary", unchecked((int)0x766a885a)), new(string.Empty, 0)],
            draft.Cells);
    }

    [Fact]
    public void Editing_to_null_or_empty_clears_a_stale_hash()
    {
        StringTableDraft draft = CreateDraft(1, 2,
            ("first", 123), ("second", 456));

        draft.SetCellValue(0, 0, null);
        draft.SetCellValue(0, 1, string.Empty);

        Assert.Equal([new(null, 0), new(string.Empty, 0)], draft.Cells);
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
        Assert.NotEqual(0, asset.Cells[0].Hash);
        Assert.NotEqual(0, asset.Cells[1].Hash);
        Assert.Equal(0, asset.Cells[4].Hash);
        Assert.Equal(0, asset.Cells[6].Hash);
        Assert.Equal(0, asset.Cells[17].Hash);
    }

    [Fact]
    public void Csv_import_hashes_retail_classtable_cells()
    {
        const string csv = "loadoutName,loadoutPrimary,loadoutPrimaryAttachment," +
            "loadoutPrimaryAttachment2,loadoutPrimaryCamo,loadoutSecondary\n";

        StringTableAsset asset = new StringTableExchange().ReadCsv(
            new StringReader(csv), "mp/classtable.csv");

        Assert.Equal(1, asset.RowCount);
        Assert.Equal(6, asset.ColumnCount);
        Assert.Equal(
            [0x3c47df33u, 0x766a885au, 0x5c3eb8fdu,
             0x2b9866d5u, 0xb30945dau, 0xc2fcb5ccu],
            asset.Cells.Select(cell => unchecked((uint)cell.Hash)));
    }

    [Theory]
    [InlineData("loadoutName", 0x3c47df33u)]
    [InlineData("LoAdOuTnAmE", 0x3c47df33u)]
    [InlineData(null, 0u)]
    [InlineData("", 0u)]
    public void Hash_uses_ascii_case_folding_and_zero_for_missing_values(
        string? value,
        uint expectedHash)
    {
        Assert.Equal(expectedHash, unchecked((uint)StringTableCell.CalculateHash(value)));
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
