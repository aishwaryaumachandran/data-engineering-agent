using System.Globalization;
using Azure.Storage.Files.DataLake;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using DataEngineeringAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace DataEngineeringAgent.Core.Services;

public class AdlsService : IAdlsService
{
    private readonly DataLakeServiceClient _client;
    private readonly ILogger<AdlsService> _logger;

    public AdlsService(DataLakeServiceClient client, ILogger<AdlsService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<byte[]> DownloadFileAsync(string container, string path)
    {
        var fs = _client.GetFileSystemClient(container);
        var file = fs.GetFileClient(path);
        var response = await file.ReadAsync();

        using var ms = new MemoryStream();
        await response.Value.Content.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task<List<string>> ListFilesAsync(string container, string prefix = "")
    {
        var fs = _client.GetFileSystemClient(container);
        var result = new List<string>();

        await foreach (var item in fs.GetPathsAsync(prefix))
        {
            result.Add(item.Name);
        }

        return result;
    }

    public async Task<Dictionary<string, SheetData>> ReadMappingSpreadsheetAsync(string path)
    {
        var data = await DownloadFileAsync("mappings", StripContainerPrefix("mappings", path));
        using var stream = new MemoryStream(data);
        using var workbook = new XLWorkbook(stream);

        var result = new Dictionary<string, SheetData>();

        foreach (var worksheet in workbook.Worksheets)
        {
            var (columns, rows) = ReadWorksheet(worksheet, maxRows: int.MaxValue);
            result[worksheet.Name] = new SheetData(columns, rows.Count, rows);
        }

        return result;
    }

    public async Task<DataSample> SampleSourceDataAsync(string path, int nRows = 100)
    {
        path = StripContainerPrefix("data", path);
        var data = await DownloadFileAsync("data", path);

        if (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            return await ReadParquetSampleAsync(data, nRows);
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ReadCsvSample(data, nRows);

        return ReadExcelSample(data, nRows);
    }

    public async Task<DataSample> ReadSparkOutputAsync(string path, int nRows = 50)
    {
        // Spark typically writes a directory of part files
        try
        {
            var files = await ListFilesAsync("output", prefix: path);
            var parquetFiles = files
                .Where(f => f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).StartsWith("_"))
                .ToList();

            if (parquetFiles.Count > 0)
            {
                var data = await DownloadFileAsync("output", parquetFiles[0]);
                return await ReadParquetSampleAsync(data, nRows);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list output directory, trying as single file");
        }

        // Fallback: single file
        var fileData = await DownloadFileAsync("output", path);
        if (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            return await ReadParquetSampleAsync(fileData, nRows);

        return ReadCsvSample(fileData, nRows);
    }

    private static (List<string> Columns, List<Dictionary<string, object?>> Rows) ReadWorksheet(
        IXLWorksheet worksheet, int maxRows)
    {
        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

        // Scan the first 20 used rows and pick the one with the most non-empty cells.
        // Header rows are typically the densest row in the preamble area because
        // title/description rows only fill 1-2 cells.
        var usedRows = worksheet.RowsUsed().Take(20).ToList();
        if (usedRows.Count == 0) return (columns, rows);

        int bestRowIndex = 0;
        int bestCount = 0;

        for (int i = 0; i < usedRows.Count; i++)
        {
            int nonEmpty = usedRows[i].CellsUsed()
                .Count(c => !string.IsNullOrWhiteSpace(c.GetString()));

            if (nonEmpty > bestCount)
            {
                bestCount = nonEmpty;
                bestRowIndex = i;
            }
        }

        // Only skip preamble if the best row has more cells than the first row
        int firstRowCount = usedRows[0].CellsUsed()
            .Count(c => !string.IsNullOrWhiteSpace(c.GetString()));
        int headerRowIndex = bestCount > firstRowCount ? bestRowIndex : 0;

        var headerRow = usedRows[headerRowIndex];

        // Read headers, skipping empty cells
        foreach (var cell in headerRow.CellsUsed())
        {
            var value = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(value))
                columns.Add(value);
        }

        // Read data rows after the header
        var dataRows = worksheet.RowsUsed()
            .Where(r => r.RowNumber() > headerRow.RowNumber())
            .Take(maxRows);

        foreach (var row in dataRows)
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < columns.Count; i++)
            {
                var cell = row.Cell(headerRow.CellsUsed().ElementAt(i).Address.ColumnNumber);
                dict[columns[i]] = cell.IsEmpty() ? null : cell.Value.ToObject();
            }
            rows.Add(dict);
        }

        return (columns, rows);
    }

    private static DataSample ReadCsvSample(byte[] data, int nRows)
    {
        using var reader = new StreamReader(new MemoryStream(data));
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        });

        csv.Read();
        csv.ReadHeader();
        var columns = csv.HeaderRecord?.ToList() ?? [];
        var dtypes = new Dictionary<string, string>();
        var sampleRows = new List<Dictionary<string, object?>>();
        int totalRows = 0;

        while (csv.Read())
        {
            totalRows++;
            if (sampleRows.Count < nRows)
            {
                var row = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var val = csv.GetField(col);
                    if (string.IsNullOrEmpty(val))
                        row[col] = null;
                    else if (long.TryParse(val, out var l))
                        row[col] = l;
                    else if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        row[col] = d;
                    else if (bool.TryParse(val, out var b))
                        row[col] = b;
                    else
                        row[col] = val;
                }
                sampleRows.Add(row);
            }
        }

        // Infer types from all sample rows
        foreach (var col in columns)
        {
            var values = sampleRows.Select(r => r.GetValueOrDefault(col)).Where(v => v is not null).ToList();
            if (values.Count == 0)
                dtypes[col] = "object";
            else if (values.All(v => v is int or long))
                dtypes[col] = "int64";
            else if (values.All(v => v is int or long or float or double or decimal))
                dtypes[col] = "float64";
            else if (values.All(v => v is bool))
                dtypes[col] = "bool";
            else
                dtypes[col] = "object";
        }

        return new DataSample(columns, dtypes, totalRows, sampleRows);
    }

    private static DataSample ReadExcelSample(byte[] data, int nRows)
    {
        using var stream = new MemoryStream(data);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        var (columns, rows) = ReadWorksheet(worksheet, nRows);

        // Total row count (all used rows minus header)
        var totalRows = worksheet.RowsUsed().Count() - 1;

        var dtypes = new Dictionary<string, string>();
        if (rows.Count > 0)
        {
            foreach (var col in columns)
            {
                var val = rows[0].GetValueOrDefault(col);
                dtypes[col] = val is double or int or long or decimal ? "float64" : "object";
            }
        }

        return new DataSample(columns, dtypes, totalRows, rows);
    }

    private static async Task<DataSample> ReadParquetSampleAsync(byte[] data, int nRows)
    {
        using var stream = new MemoryStream(data);
        using var reader = await ParquetReader.CreateAsync(stream);

        var schema = reader.Schema;
        var columns = schema.Fields.Select(f => f.Name).ToList();
        var dtypes = new Dictionary<string, string>();

        foreach (var field in schema.Fields)
        {
            if (field is DataField df)
            {
                dtypes[field.Name] = df.ClrType switch
                {
                    var t when t == typeof(int) || t == typeof(long) || t == typeof(short) => "int64",
                    var t when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "float64",
                    var t when t == typeof(bool) => "bool",
                    var t when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "datetime64",
                    _ => "object"
                };
            }
        }

        var sampleRows = new List<Dictionary<string, object?>>();
        int totalRows = 0;

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);
            var rowGroupLength = (int)rowGroupReader.RowCount;
            totalRows += rowGroupLength;

            if (sampleRows.Count >= nRows) continue;

            // Read all columns for this row group
            var columnData = new Dictionary<string, Array>();
            foreach (var field in schema.Fields)
            {
                if (field is DataField df)
                {
                    var col = await rowGroupReader.ReadColumnAsync(df);
                    columnData[field.Name] = col.Data;
                }
            }

            var rowsToRead = Math.Min(rowGroupLength, nRows - sampleRows.Count);
            for (int r = 0; r < rowsToRead; r++)
            {
                var row = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    if (columnData.TryGetValue(col, out var arr) && r < arr.Length)
                        row[col] = arr.GetValue(r);
                    else
                        row[col] = null;
                }
                sampleRows.Add(row);
            }
        }

        return new DataSample(columns, dtypes, totalRows, sampleRows);
    }

    /// <summary>
    /// Strip the container name from a path if it starts with "container/".
    /// Callers send paths like "mappings/CLIENT_001/file.xlsx" but the service
    /// already hardcodes the container, so the blob path is "CLIENT_001/file.xlsx".
    /// </summary>
    private static string StripContainerPrefix(string container, string path)
    {
        var prefix = container + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path;
    }

    // Helper extension for ClosedXML cell values
}

internal static class XlCellValueExtensions
{
    public static object? ToObject(this XLCellValue value)
    {
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => value.GetBoolean(),
            XLDataType.Number => value.GetNumber(),
            XLDataType.Text => value.GetText(),
            XLDataType.DateTime => value.GetDateTime(),
            XLDataType.TimeSpan => value.GetTimeSpan(),
            _ => value.ToString()
        };
    }
}
