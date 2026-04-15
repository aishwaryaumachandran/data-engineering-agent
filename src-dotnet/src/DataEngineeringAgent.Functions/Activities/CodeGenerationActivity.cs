using System.Text.RegularExpressions;
using DataEngineeringAgent.Core.Prompts;
using DataEngineeringAgent.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataEngineeringAgent.Functions.Activities;

public class CodeGenerationActivity
{
    private readonly IAdlsService _adls;
    private readonly IOpenAiService _openAi;
    private readonly ILogger<CodeGenerationActivity> _logger;
    private readonly bool _isLocal;

    // Patterns that indicate the LLM generated boilerplate instead of just the config
    private static readonly string[] ForbiddenPatterns = ["import ", "spark.read", ".write.parquet", "spark.createDataFrame"];

    public CodeGenerationActivity(IAdlsService adls, IOpenAiService openAi, ILogger<CodeGenerationActivity> logger, IConfiguration config)
    {
        _adls = adls;
        _openAi = openAi;
        _logger = logger;
        _isLocal = string.Equals(config["RunMode"], "Local", StringComparison.OrdinalIgnoreCase);
    }

    [Function(nameof(CodeGeneration))]
    public async Task<string> CodeGeneration([ActivityTrigger] CodeGenerationInput input)
    {
        var sourceColumns = "";
        if (!string.IsNullOrEmpty(input.DataPath))
        {
            try
            {
                var sample = await _adls.SampleSourceDataAsync(input.DataPath, nRows: 5);
                sourceColumns = string.Join(", ", sample.Columns);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not sample source data for column names");
            }
        }

        var prompt = SystemPrompts.ConfigGeneration
            .Replace("{pseudocode}", input.Pseudocode)
            .Replace("{source_columns}", sourceColumns);

        var configBlock = await _openAi.RunAgentCodeAsync(prompt, "Generate the TRANSFORM_CONFIG dict.");

        // Strip markdown code fences if present
        configBlock = StripCodeFences(configBlock);

        // Validate: must contain TRANSFORM_CONFIG, must NOT contain boilerplate
        if (!configBlock.Contains("TRANSFORM_CONFIG"))
            throw new InvalidOperationException("LLM output does not contain TRANSFORM_CONFIG");

        foreach (var pattern in ForbiddenPatterns)
        {
            if (configBlock.Contains(pattern))
                throw new InvalidOperationException(
                    $"LLM generated boilerplate code (found '{pattern}'). Expected only TRANSFORM_CONFIG dict.");
        }

        // Assemble full notebook: inject config + paths into template
        var template = _isLocal ? SystemPrompts.LocalSparkTemplate : SystemPrompts.SparkTemplate;
        var notebook = template
            .Replace("{input_path}", input.InputPath)
            .Replace("{output_path}", input.OutputPath)
            .Replace("{config_block}", configBlock);

        _logger.LogInformation("Generated PySpark notebook for {ClientId} ({Chars} chars, config {ConfigChars} chars)",
            input.ClientId, notebook.Length, configBlock.Length);
        return notebook;
    }

    private static string StripCodeFences(string text)
    {
        // Remove ```python ... ``` or ``` ... ``` wrappers
        var match = Regex.Match(text, @"```(?:python)?\s*\n([\s\S]*?)\n\s*```", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }
}

public record CodeGenerationInput(
    string ClientId,
    string Pseudocode,
    string InputPath,
    string OutputPath,
    string DataPath = "");
