using Prism.Application.Export;

namespace Prism.Tests.Export;

public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+SUM(A1)", "\"'+SUM(A1)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tcmd", "\"'\tcmd\"")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    public void A_cell_that_starts_like_a_formula_gets_a_leading_apostrophe_inside_the_quotes(string cell, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(cell));
    }

    [Theory]
    [InlineData("=\"x\"+1", "\"'=\"\"x\"\"+1\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("\"=quoted\"", "\"\"\"=quoted\"\"\"")]
    public void Embedded_quotes_are_doubled_with_and_without_a_formula_start(string cell, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(cell));
    }

    [Theory]
    [InlineData(null, "\"\"")]
    [InlineData("", "\"\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("mid=dle", "\"mid=dle\"")]
    public void Every_other_cell_is_only_quoted(string? cell, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(cell));
    }

    [Fact]
    public void A_file_has_a_quoted_header_and_one_line_per_row_ending_in_crlf()
    {
        string csv = CsvWriter.Write(["A", "B"], [["1", "=x"], ["2", null]]);

        Assert.Equal("\"A\",\"B\"\r\n\"1\",\"'=x\"\r\n\"2\",\"\"\r\n", csv);
    }

    [Theory]
    [InlineData("-12.50", "\"-12.50\"")]
    [InlineData("0.00", "\"0.00\"")]
    public void A_number_cell_keeps_its_minus_sign_and_gets_no_apostrophe(string formatted, string expected)
    {
        decimal value = decimal.Parse(formatted, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, CsvWriter.Cell(CsvCell.Number(value)));
    }

    [Fact]
    public void A_text_cell_that_looks_like_a_negative_number_is_still_neutralized()
    {
        Assert.Equal("\"'-12.50\"", CsvWriter.Cell(new CsvCell("-12.50")));
    }

    [Fact]
    public void The_buffer_writer_output_is_the_same_UTF8_bytes_as_the_string_version_including_non_ascii_and_quotes()
    {
        string[] header = ["Payer", "Note"];
        CsvCell[][] rows = [["Sjögren – 日本語 😀", "say \"=hi\""], [CsvCell.Number(-12.5m), "=x"]];
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();

        CsvWriter.Write(buffer, header, rows);

        Assert.Equal(CsvWriter.Write(header, rows), System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.Equal("\"Payer\",\"Note\"\r\n\"Sjögren – 日本語 😀\",\"say \"\"=hi\"\"\"\r\n\"-12.50\",\"'=x\"\r\n", CsvWriter.Write(header, rows));
    }
}
