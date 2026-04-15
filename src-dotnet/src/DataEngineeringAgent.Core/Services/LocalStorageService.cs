using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using DataEngineeringAgent.Core.Configuration;
using DataEngineeringAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace DataEngineeringAgent.Core.Services;

public class LocalStorageService : IAdlsService
{
    private readonly LocalOptions _opts;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IOptions<LocalOptions> opts, ILogger<LocalStorageService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public Task<byte[]> DownloadFileAsync(string container, string path)
    {
        var root = ResolveRoot(container);
        var fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        _logger.LogDebug("Reading local file: {Path}", fullPath);
        return File.ReadAllBytesAsync(fullPath);
    }

    public Task<List<string>> ListFilesAsync(string container, string prefix = "")
    {
        var root = ResolveRoot(container);
        var dir = string.IsNullOrEmpty(prefix) ? root : Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(dir))
            return Task.FromResult(new List<string>());

        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        return Task.FromResult(files);
    }

    public Task<Dictionary<string, SheetData>> ReadMappingSpreadsheetAsync(string path)
    {
        var fullPath = ResolvePath("mappings", path);
        _logger.LogInformation("Reading mapping spreadsheet: {Path}", fullPath);

        using var stream = File.OpenRead(fullPath);
        using var workbook = new XLWorkbook(stream);

        var result = new Dictionary<string, SheetData>();
        foreach (var worksheet in workbook.Worksheets)
        {
            var (columns, rows) = ReadWorksheet(worksheet, maxRows: int.MaxValue);
            result[worksheet.Name] = new SheetData(columns, rows.Count, rows);
        }

        return Task.FromResult(result);
    }

    public async Task<DataSample> SampleSourceDataAsync(string path, int nRows = 100)
    {
        var fullPath = ResolvePath("data", path);
        _logger.LogInformation("Sampling source data: {Path}", fullPath);
        var data = await File.ReadAllBytesAsync(fullPath);

        if (fullPath.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            return await ReadParquetSampleAsync(data, nRows);
        if (fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ReadCsvSample(data, nRows);

        return ReadExcelSample(data, nRows);
    }

    public async Task<DataSample> ReadSparkOutputAsync(string path, int nRows = 50)
    {
        var outputDir = Path.Combine(_opts.OutputRoot, path.Replace('/', Path.DirectorySeparatorChar));

        // Spark writes a directory of part files
        if (Directory.Exists(outputDir))
        {
            var parquetFiles = Directory.GetFiles(outputDir, "*.parquet", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_'))
                .ToList();

            if (parquetFiles.Count > 0)
            {
                var data = await File.ReadAllBytesAsync(parquetFiles[0]);
                return await ReadParquetSampleAsync(data, nRows);
            }
        }

        // Fallback: single file
        var fileData = await File.ReadAllBytesAsync(outputDir);
        if (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            return await ReadParquetSampleAsync(fileData, nRows);

        return ReadCsvSample(fileData, nRows);
    }

    private string ResolveRoot(string container) => container.ToLowerInvariant() switch
    {
        "data" => _opts.DataRoot,
        "mappings" => _opts.DataRoot,
        "output" => _opts.OutputRoot,
        _ => Path.Combine(_opts.DataRoot, container),
    };

    private string ResolvePath(string container, string path)
    {
        // In local mode, paths are relative to DataRoot (e.g. "CLIENT_001/mapping/mapping.xlsm")
        // Strip container prefix if present (legacy ADLS convention: "mappings/CLIENT_001/...")
        var prefix = container + "/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            path = path[prefix.Length..];

        // Also strip alternate container names (mappings→mapping, data→client)
        var altPrefixes = new[] { "mappings/", "data/" };
        foreach (var alt in altPrefixes)
        {
            if (path.StartsWith(alt, StringComparison.OrdinalIgnoreCase))
            {
                path = path[alt.Length..];
                break;
            }
        }

        return Path.Combine(_opts.DataRoot, path.Replace('/', Path.DirectorySeparatorChar));
    }

    // === File reading helpers (same logic as AdlsService) ===

    private static (List<string> Columns, List<Dictionary<string, object?>> Rows) ReadWorksheet(
        IXLWorksheet worksheet, int maxRows)
    {
        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

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

        int firstRowCount = usedRows[0].CellsUsed()
            .Count(c => !string.IsNullOrWhiteSpace(c.GetString()));
        int headerRowIndex = bestCount > firstRowCount ? bestRowIndex : 0;
        var headerRow = usedRows[headerRowIndex];

        foreach (var cell in headerRow.CellsUsed())
        {
            var value = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(value))
                columns.Add(value);
        }

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
}
