using DataEngineeringAgent.Core.Models;
using DataEngineeringAgent.Core.Services;
using DataEngineeringAgent.Functions.Activities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DataEngineeringAgent.Functions.Orchestrators;

public static class TransformOrchestrator
{
    private const int MaxCodeRetries = 5;

    [Function(nameof(TransformOrchestration))]
    public static async Task<TransformResult> TransformOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger(nameof(TransformOrchestrator));
        var input = context.GetInput<TransformRequest>()!;
        var threadId = context.InstanceId;

        // Use replay-safe clock
        var outputPath = $"{input.ClientId}/{context.CurrentUtcDateTime:yyyyMMdd_HHmmss}";

        string? pseudocode = null;
        string? pysparkCode = null;

        // --- Phase 1: Change Detection ---
        await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
            new LogMessageInput(threadId, input.ClientId, "change_detection", "agent",
                "Checking if existing transformation can be reused..."));

        var detection = await context.CallActivityAsync<ChangeDetectionResult>(
            nameof(ChangeDetectionActivity.ChangeDetection),
            new ChangeDetectionInput(input.ClientId, input.MappingPath, input.DataPath));

        if (!detection.NeedsRegeneration && detection.ExistingCode is not null)
        {
            await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                new LogMessageInput(threadId, input.ClientId, "change_detection", "agent",
                    $"Reusing existing transformation. Reason: {detection.Reason}"));

            pysparkCode = detection.ExistingCode.PySparkCode;
            pseudocode = detection.ExistingCode.Pseudocode;

            // Update output path in reused code
            pysparkCode = OutputPathRewriter.Rewrite(pysparkCode, input.AdlsAccountName, outputPath);
        }
        else
        {
            await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                new LogMessageInput(threadId, input.ClientId, "change_detection", "agent",
                    $"Regeneration needed. Reason: {detection.Reason}"));
        }

        // --- Phases 2-6 loop (output rejection loops back here) ---
        while (true)
        {
            // --- Phase 2: Profiling + Pseudocode ---
            if (pysparkCode is null)
            {
                pseudocode = await context.CallActivityAsync<string>(
                    nameof(ProfilingActivity.Profiling),
                    new ProfilingInput(input.ClientId, input.MappingPath, input.DataPath));

                await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                    new LogMessageInput(threadId, input.ClientId, "pseudocode_review", "agent", pseudocode));

                // --- Phase 3: Auditor Review (loop) ---
                while (true)
                {
                    var review = await context.WaitForExternalEvent<ReviewEvent>("review");

                    await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                        new LogMessageInput(threadId, input.ClientId, "pseudocode_review", "auditor",
                            review.Approved ? "Approved" : $"Feedback: {review.Feedback}"));

                    if (review.Approved) break;

                    pseudocode = await context.CallActivityAsync<string>(
                        nameof(RevisePseudocodeActivity.RevisePseudocode),
                        new RevisePseudocodeInput(pseudocode, review.Feedback ?? ""));

                    await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                        new LogMessageInput(threadId, input.ClientId, "pseudocode_review", "agent", pseudocode));
                }

                // --- Phase 4a: Code Generation ---
                var sa = input.AdlsAccountName;
                var isLocal = string.Equals(sa, "local", StringComparison.OrdinalIgnoreCase);

                // DataPath may include container prefix (e.g. "data/CLIENT_001/file.xlsx")
                // but the abfss URL already specifies the container, so strip it
                var blobPath = input.DataPath.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
                    ? input.DataPath["data/".Length..] : input.DataPath;

                string inputUrl, outputUrl;
                if (isLocal)
                {
                    var dataRoot = Environment.GetEnvironmentVariable("Local__DataRoot") ?? "input_data";
                    var outputRoot = Environment.GetEnvironmentVariable("Local__OutputRoot") ?? "output";
                    // Use forward slashes so the path is valid in Python string literals
                    inputUrl = Path.Combine(dataRoot, blobPath.Replace('/', Path.DirectorySeparatorChar))
                        .Replace('\\', '/');
                    outputUrl = Path.Combine(outputRoot, outputPath.Replace('/', Path.DirectorySeparatorChar))
                        .Replace('\\', '/');
                }
                else
                {
                    inputUrl = $"abfss://data@{sa}.dfs.core.windows.net/{blobPath}";
                    outputUrl = $"abfss://output@{sa}.dfs.core.windows.net/{outputPath}";
                }

                pysparkCode = await context.CallActivityAsync<string>(
                    nameof(CodeGenerationActivity.CodeGeneration),
                    new CodeGenerationInput(
                        input.ClientId,
                        pseudocode,
                        inputUrl,
                        outputUrl,
                        input.DataPath));
            }

            // --- Phase 4b + 5: Execution + Integrity (retry loop) ---
            var executionSucceeded = false;
            for (int attempt = 1; attempt <= MaxCodeRetries; attempt++)
            {
                await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                    new LogMessageInput(threadId, input.ClientId, "code_generation", "agent",
                        $"Executing transformation (attempt {attempt}/{MaxCodeRetries})..."));

                var sparkResult = await context.CallActivityAsync<SparkExecutionResult>(
                    nameof(SparkExecutionActivity.SparkExecution),
                    new SparkExecutionInput(pysparkCode, input.ClientId));

                if (!sparkResult.Success)
                {
                    if (attempt < MaxCodeRetries)
                    {
                        pysparkCode = await context.CallActivityAsync<string>(
                            nameof(FixCodeActivity.FixCode),
                            new FixCodeInput(pysparkCode, sparkResult.ErrorLog));
                        continue;
                    }

                    await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                        new LogMessageInput(threadId, input.ClientId, "code_generation", "agent",
                            $"Transformation failed after {MaxCodeRetries} attempts. Error: {sparkResult.ErrorLog}"));
                    return new TransformResult("failed", null, sparkResult.ErrorLog);
                }

                // Phase 5: Integrity Checks
                var integrity = await context.CallActivityAsync<IntegrityReport>(
                    nameof(IntegrityChecksActivity.IntegrityChecks),
                    new IntegrityChecksInput(outputPath));

                if (integrity.OverallPass)
                {
                    executionSucceeded = true;
                    break;
                }

                if (attempt < MaxCodeRetries)
                {
                    var errorContext = string.Join("; ", integrity.Errors);
                    pysparkCode = await context.CallActivityAsync<string>(
                        nameof(FixCodeActivity.FixCode),
                        new FixCodeInput(pysparkCode, $"Integrity check failures: {errorContext}"));
                }
                else
                {
                    await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                        new LogMessageInput(threadId, input.ClientId, "code_generation", "agent",
                            $"Integrity checks failed after {MaxCodeRetries} attempts: {string.Join(", ", integrity.Errors)}"));
                    return new TransformResult("failed", null, $"Integrity failures: {string.Join(", ", integrity.Errors)}");
                }
            }

            if (!executionSucceeded)
                return new TransformResult("failed", null, "Execution did not succeed");

            // --- Phase 6: Auditor Review of Output ---
            await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                new LogMessageInput(threadId, input.ClientId, "output_review", "agent",
                    $"Transformation complete. Output at: {outputPath}\nIntegrity checks: PASSED\nPlease review the output."));

            var outputReview = await context.WaitForExternalEvent<ReviewEvent>("review");

            await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                new LogMessageInput(threadId, input.ClientId, "output_review", "auditor",
                    outputReview.Approved ? "Approved" : $"Rejected: {outputReview.Feedback}"));

            if (outputReview.Approved)
                break; // Exit the while loop — proceed to save

            // Output rejected — loop back to Phase 2
            await context.CallActivityAsync(nameof(LogMessageActivity.LogMessage),
                new LogMessageInput(threadId, input.ClientId, "output_review", "agent",
                    "Output rejected. Returning to pseudocode revision..."));
            pysparkCode = null; // Force regeneration
        }

        // --- Save approved code ---
        await context.CallActivityAsync(nameof(SaveCodeActivity.SaveCode),
            new SaveCodeInput(input.ClientId, pseudocode ?? "", pysparkCode!));

        return new TransformResult("completed", outputPath, null);
    }
}

public record TransformResult(string Status, string? OutputPath, string? Error);
