using System.Buffers;
using System.Globalization;
using System.Text;

namespace Prism.Application.Export;

/// <summary>
/// The one CSV writer. Every cell is quoted, embedded quotes are doubled, and a text cell that a spreadsheet could read as a formula
/// (it starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return) gets a leading apostrophe inside the quotes.
/// A number cell (<see cref="CsvCell.Number"/>) is quoted but not neutralized: its leading minus is a sign.
/// Output is UTF-8, written to an <see cref="IBufferWriter{T}"/> so a caller can stream it without building a string.
/// </summary>
public static class CsvWriter
{
    private static readonly byte[] LineEnd = "\r\n"u8.ToArray();

    /// <summary>Writes the header and every row as UTF-8 into <paramref name="output"/>.</summary>
    public static void Write(IBufferWriter<byte> output, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<CsvCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        for (int i = 0; i < header.Count; i++)
        {
            WriteSeparator(output, i);
            WriteCell(output, new CsvCell(header[i]));
        }

        output.Write(LineEnd);
        foreach (IReadOnlyList<CsvCell> row in rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                WriteSeparator(output, i);
                WriteCell(output, row[i]);
            }

            output.Write(LineEnd);
        }
    }

    /// <summary>The whole file as a string. For tests and small files; a download should use the buffer-writer overload.</summary>
    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<CsvCell>> rows)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Write(buffer, header, rows);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>One text cell, quoted and neutralized.</summary>
    public static string Cell(string? value) => Cell(new CsvCell(value));

    /// <summary>One cell, quoted. Text is neutralized; a number is not.</summary>
    public static string Cell(CsvCell cell)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteCell(buffer, cell);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCell(IBufferWriter<byte> output, CsvCell cell)
    {
        ReadOnlySpan<char> text = cell.Text;
        WriteAscii(output, '"');
        if (!cell.IsNumber && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            WriteAscii(output, '\'');
        }

        // Write up to each embedded quote, then the quote twice. A split never lands inside a surrogate pair.
        while (true)
        {
            int quote = text.IndexOf('"');
            if (quote < 0)
            {
                WriteText(output, text);
                break;
            }

            WriteText(output, text[..(quote + 1)]);
            WriteAscii(output, '"');
            text = text[(quote + 1)..];
        }

        WriteAscii(output, '"');
    }

    private static void WriteSeparator(IBufferWriter<byte> output, int index)
    {
        if (index > 0)
        {
            WriteAscii(output, ',');
        }
    }

    private static void WriteAscii(IBufferWriter<byte> output, char c)
    {
        Span<byte> span = output.GetSpan(1);
        span[0] = (byte)c;
        output.Advance(1);
    }

    private static void WriteText(IBufferWriter<byte> output, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        Span<byte> span = output.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
        output.Advance(Encoding.UTF8.GetBytes(text, span));
    }
}

/// <summary>A cell value: text (neutralized on write) or a number (written as is). Build a number only from a <see cref="decimal"/>.</summary>
public readonly record struct CsvCell(string? Text, bool IsNumber = false)
{
    public static implicit operator CsvCell(string? text) => new(text);

    public static CsvCell Number(decimal value) => new(value.ToString("0.00", CultureInfo.InvariantCulture), IsNumber: true);
}
