using Skoleni.Data.Parsing;

namespace Skoleni.Tests;

public class CsvTableTests
{
    [Fact]
    public void Detects_semicolon_and_comma_from_header()
    {
        Assert.Equal("1", CsvTable.Parse("a;b\n1;2")[0]["a"]);
        Assert.Equal("2", CsvTable.Parse("a,b\n1,2")[0]["b"]);
    }

    [Fact]
    public void Handles_quotes_escaped_quotes_and_newlines_inside_fields()
    {
        var rows = CsvTable.Parse("id;note\nZ1;\"řádek 1\nřádek 2\"\nZ2;\"říká „ano“ a \"\"ne\"\"\"\nZ3;ok");
        Assert.Equal(3, rows.Count);
        Assert.Equal("řádek 1\nřádek 2", rows[0]["note"]);
        Assert.Equal("říká „ano“ a \"ne\"", rows[1]["note"]);
        Assert.Equal("Z3", rows[2]["id"]);
    }

    [Fact]
    public void Reports_physical_line_numbers_even_after_multiline_fields()
    {
        var rows = CsvTable.Parse("id;note\nZ1;\"a\nb\"\nZ2;x");
        Assert.Equal(2, rows[0].Line);
        Assert.Equal(4, rows[1].Line);
    }

    [Fact]
    public void Tolerates_bom_crlf_blank_lines_and_ragged_rows()
    {
        var rows = CsvTable.Parse("\uFEFFa;b;c\r\n1;2\r\n\r\n;;\r\n3;4;5;6\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("", rows[0]["c"]);   // short row padded
        Assert.Equal("5", rows[1]["c"]);  // long row truncated
    }

    [Fact]
    public void Unknown_column_reads_as_empty() => Assert.Equal("", CsvTable.Parse("a\n1")[0]["neexistuje"]);
}

public class DateParsingTests
{
    [Theory]
    [InlineData("2019-01-06", 2019, 1, 6)]
    [InlineData("6. 1. 2019", 2019, 1, 6)]
    [InlineData("06.01.2019", 2019, 1, 6)]
    [InlineData("24. 1. 2024", 2024, 1, 24)]
    [InlineData("43552", 2019, 3, 28)]
    [InlineData(" 2019-01-06 ", 2019, 1, 6)]
    public void Parses_all_formats_seen_in_exports(string raw, int y, int m, int d)
    {
        Assert.True(CzechDate.TryParse(raw, out var date, out _));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Theory]
    [InlineData("31. 9. 2026")]   // September has 30 days - must not be "fixed" to 1 Oct
    [InlineData("30. 2. 2025")]
    [InlineData("")]
    [InlineData("brzy")]
    [InlineData("2019-13-01")]
    public void Rejects_impossible_or_unreadable_dates(string raw) => Assert.False(CzechDate.TryParse(raw, out _, out _));

    [Fact]
    public void Reports_which_format_was_used()
    {
        CzechDate.TryParse("2019-01-06", out _, out var iso);
        CzechDate.TryParse("6. 1. 2019", out _, out var cz);
        CzechDate.TryParse("43552", out _, out var xl);
        Assert.Equal((CzechDate.Kind.Iso, CzechDate.Kind.Czech, CzechDate.Kind.ExcelSerial), (iso, cz, xl));
    }
}

public class MoneyAndTextTests
{
    [Theory]
    [InlineData("1 500,00", 1500)]
    [InlineData("1\u00a0500,00", 1500)]
    [InlineData("2 100 Kč", 2100)]
    [InlineData("380,- Kč", 380)]
    [InlineData("zdarma", 0)]
    [InlineData("-600,00", -600)]
    [InlineData("450", 450)]
    public void Parses_prices(string raw, int expected) => Assert.Equal(expected, Money.Parse(raw));

    [Theory]
    [InlineData("")]
    [InlineData("neuvedeno")]
    public void Unreadable_price_is_null(string raw) => Assert.Null(Money.Parse(raw));

    [Theory]
    [InlineData("  LUCIE", "Lucie")]
    [InlineData("růžička", "Růžička")]
    [InlineData("nováková-malá", "Nováková-Malá")]
    [InlineData("Martina ", "Martina")]
    public void Title_cases_names(string raw, string expected) => Assert.Equal(expected, Text.TitleCase(raw));

    [Theory]
    [InlineData("Správa budov", "SPRÁVA BUDOV")]
    [InlineData("Správa budov", "Sprava budov")]
    [InlineData("Laboratoře", "laboratore")]
    public void Key_ignores_case_and_diacritics(string a, string b) => Assert.Equal(Text.Key(a), Text.Key(b));

    [Fact]
    public void Key_keeps_different_words_different() => Assert.NotEqual(Text.Key("Technik"), Text.Key("Technik údržby"));
}
