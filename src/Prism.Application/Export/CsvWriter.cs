using System.Text;

namespace Prism.Application.Export;

/// <summary>
/// The one CSV writer. Every cell is quoted, embedded quotes are doubled, and a cell that a spreadsheet could read as a formula
/// (it starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return) gets a leading apostrophe inside the quotes.
/// </summary>
public static class CsvWriter
{
    private const string LineEnd = "\r\n";

    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (IReadOnlyList<string?> row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    /// <summary>One cell, quoted and neutralized.</summary>
    public static string Cell(string? value)
    {
        string text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Cell(cells[i]));
        }

        builder.Append(LineEnd);
    }
}
