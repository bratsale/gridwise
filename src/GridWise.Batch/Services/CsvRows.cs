using System.Text;

namespace GridWise.Batch.Services;

public static class CsvRows
{
    /// <summary>
    /// The data rows of a CSV: the raw text of every non-blank line after the header, with its 1-based line number in the original file
    /// (the header is line 1 and blank lines are skipped but counted). A UTF-8 BOM is dropped. The rows are not parsed or validated.
    /// </summary>
    public static IEnumerable<(int Line, string Text)> Read(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        reader.ReadLine(); // the header
        var line = 1;
        while (reader.ReadLine() is { } text)
        {
            line++;
            if (text.Length > 0)
                yield return (line, text);
        }
    }
}
