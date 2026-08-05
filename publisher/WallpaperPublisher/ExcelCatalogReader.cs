using System.Globalization;
using ClosedXML.Excel;

namespace WallpaperPublisher;

public sealed class ExcelCatalogReader
{
    public IReadOnlyList<SourcePlace> Read(string workbookPath, string worksheetName)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var worksheet = workbook.Worksheet(worksheetName);
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidDataException($"Worksheet '{worksheetName}' is empty.");
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();

        var columns = headerRow.CellsUsed()
            .ToDictionary(cell => Clean(cell.GetFormattedString()), cell => cell.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        string Text(IXLRow row, string name)
        {
            if (!columns.TryGetValue(name, out var column))
                throw new InvalidDataException($"Required column '{name}' is missing on worksheet '{worksheetName}'.");
            return Clean(row.Cell(column).GetFormattedString());
        }

        string OptionalText(IXLRow row, string name)
        {
            return columns.TryGetValue(name, out var column)
                ? Clean(row.Cell(column).GetFormattedString())
                : string.Empty;
        }

        double Number(IXLRow row, string name)
        {
            var raw = Text(row, name);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)) return current;
            return 0;
        }

        int? OptionalInteger(IXLRow row, string name)
        {
            var raw = Text(row, name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
        }

        var rows = new List<SourcePlace>();
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var id = Text(row, "Place ID");
            if (string.IsNullOrWhiteSpace(id)) continue;

            rows.Add(new SourcePlace(
                rowNumber,
                id,
                Text(row, "Country"),
                Text(row, "Region"),
                Text(row, "Place title"),
                Text(row, "Short description"),
                Text(row, "Full description"),
                Number(row, "Latitude"),
                Number(row, "Longitude"),
                OptionalInteger(row, "Zoom"),
                OptionalText(row, "Source URL"),
                Text(row, "Image filename"),
                Text(row, "Image status"),
                Text(row, "Imagery date"),
                Text(row, "Date status"),
                Text(row, "Attribution"),
                Text(row, "Tags"),
                Text(row, "Aesthetics"),
                Text(row, "Story"),
                OptionalInteger(row, "Revision") ?? 1,
                Text(row, "Validation")));
        }

        return rows;
    }

    private static string Clean(string? value)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean == "0" ? string.Empty : clean;
    }
}
