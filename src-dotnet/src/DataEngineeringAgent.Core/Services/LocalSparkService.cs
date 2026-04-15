using System.Diagnostics;
using DataEngineeringAgent.Core.Configuration;
using DataEngineeringAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataEngineeringAgent.Core.Services;

public class LocalSparkService : IDatabricksService
{
    private const int TimeoutSeconds = 600;

    private readonly LocalOptions _opts;
    private readonly ILogger<LocalSparkService> _logger;

    public LocalSparkService(IOptions<LocalOptions> opts, ILogger<LocalSparkService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<string> SubmitSparkJobAsync(string pysparkCode, string clientId = "")
    {
        // Write PySpark code to a temp file and run spark-submit
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var scriptDir = Path.Combine(_opts.OutputRoot, "_spark_scripts");
        Directory.CreateDirectory(scriptDir);

        var scriptPath = Path.Combine(scriptDir, $"transform_{jobId}.py");
        await File.WriteAllTextAsync(scriptPath, pysparkCode);

        _logger.LogInformation("Wrote Spark script: {Path}", scriptPath);
        return $"{jobId}|{scriptPath}";
    }

    public Task<SparkRunStatus> GetRunStatusAsync(string runId)
    {
        // Not used in local mode — ExecuteSparkJobAsync runs synchronously
        return Task.FromResult(new SparkRunStatus("TERMINATED", "SUCCESS", "", true, true));
    }

    public async Task<SparkExecutionResult> ExecuteSparkJobAsync(string pysparkCode, string clientId)
    {
        var runId = await SubmitSparkJobAsync(pysparkCode, clientId);
        var scriptPath = runId.Split('|')[1];

        _logger.LogInformation("Running spark-submit locally for {ClientId}", clientId);

        var psi = new ProcessStartInfo
        {
            FileName = _opts.SparkSubmitPath,
            Arguments = $"--master local[1] \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Ensure SPARK_HOME, PYSPARK_PYTHON, HADOOP_HOME are set so spark-submit can find jars
        if (!string.IsNullOrEmpty(_opts.SparkHome))
            psi.Environment["SPARK_HOME"] = _opts.SparkHome;
        if (!string.IsNullOrEmpty(_opts.PythonPath))
            psi.Environment["PYSPARK_PYTHON"] = _opts.PythonPath;
        if (!string.IsNullOrEmpty(_opts.HadoopHome))
            psi.Environment["HADOOP_HOME"] = _opts.HadoopHome;

        using var process = new Process { StartInfo = psi };
        var stdout = new List<string>();
        var stderr = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.Add(e.Data);
                _logger.LogDebug("[spark-submit] {Line}", e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.Add(e.Data);
                // Spark logs INFO/WARN to stderr — only log actual errors at warning level
                if (e.Data.Contains("ERROR") || e.Data.Contains("Exception"))
                    _logger.LogWarning("[spark-submit] {Line}", e.Data);
                else
                    _logger.LogDebug("[spark-submit] {Line}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = await WaitForExitAsync(process, TimeoutSeconds);

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            return new SparkExecutionResult(false, runId, $"spark-submit timed out after {TimeoutSeconds}s");
        }

        if (process.ExitCode != 0)
        {
            var errorLog = string.Join("\n", stderr.TakeLast(50));
            _logger.LogWarning("spark-submit failed with exit code {ExitCode}", process.ExitCode);
            return new SparkExecutionResult(false, runId, errorLog);
        }

        _logger.LogInformation("spark-submit completed successfully");
        return new SparkExecutionResult(true, runId, "");
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
