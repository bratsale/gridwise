using System.Text;

namespace GridWise.Batch.Services;

/// <summary>Checks only what is instant about an uploaded CSV: the header and the number of data rows. Row contents are not looked at.</summary>
public static class CsvInspector
{
    public const int MaxRows = 100_000;

    private static readonly string[] ExpectedHeader = ["customer_id", "consumption_kwh", "tax_group"];

    /// <returns>The number of data rows, or null with an error message when the file is rejected.</returns>
    public static int? CountRows(byte[] content, out string? error)
    {
        error = null;

        // StreamReader drops a UTF-8 BOM and ReadLine accepts both \n and \r\n.
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var header = reader.ReadLine();
        if (header is null || !IsExpectedHeader(header))
        {
            error = $"The first line must be the header {string.Join(",", ExpectedHeader)}.";
            return null;
        }

        var rows = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            if (++rows > MaxRows)
            {
                error = $"The file has more than {MaxRows} data rows.";
                return null;
            }
        }

        if (rows == 0)
        {
            error = "The file has a header but no data rows.";
            return null;
        }

        return rows;
    }

    private static bool IsExpectedHeader(string line)
    {
        var columns = line.Split(',');
        return columns.Length == ExpectedHeader.Length
            && columns.Zip(ExpectedHeader).All(p => string.Equals(p.First.Trim(), p.Second, StringComparison.OrdinalIgnoreCase));
    }
}
