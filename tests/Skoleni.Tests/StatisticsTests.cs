using Skoleni.Core.Domain;
using Skoleni.Core.Models;
using static Skoleni.Tests.Support.Build;

namespace Skoleni.Tests;

public class StatisticsTests
{
    [Fact]
    public void Parental_leave_is_not_counted_and_shares_are_computed_from_counted_only()
    {
        var kyber = Course("KYBER", 12);
        var ds = Set([Emp("E1"), Emp("E2"), Emp("E3", state: EmployeeState.ParentalLeave)], [kyber], [Done("E1", "KYBER", Today.AddMonths(-1))]);
        var counted = Statistics.Counted(Compliance.Compute(ds, Today, 90), Today).ToList();
        var counts = StatusCounts.Of(counted);
        Assert.Equal(2, counts.Total);
        Assert.Equal(1, counts.Valid);
        Assert.Equal(1, counts.Missing);
        Assert.Equal(0.5, counts.OkShare);
    }

    [Fact]
    public void Empty_set_is_fully_ok_not_a_division_by_zero() => Assert.Equal(1.0, StatusCounts.Of([]).OkShare);
}

public class CsvWriterTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("@SUM(1)", "'@SUM(1)")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("normální", "normální")]
    [InlineData(null, "")]
    public void Escapes_formulas_and_separators(string? input, string expected) => Assert.Equal(expected, CsvWriter.Escape(input));

    [Fact]
    public void Writes_header_and_rows_with_crlf()
    {
        var csv = CsvWriter.Write(["a", "b"], [["1", "=x"], ["2", "3"]]);
        Assert.Equal("a;b\r\n1;'=x\r\n2;3\r\n", csv);
    }

    [Fact]
    public void Data_uri_starts_with_utf8_bom_so_excel_reads_czech_characters()
    {
        var uri = CsvWriter.ToDataUri("ž");
        var bytes = Convert.FromBase64String(uri[(uri.IndexOf("base64,", StringComparison.Ordinal) + 7)..]);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }
}
