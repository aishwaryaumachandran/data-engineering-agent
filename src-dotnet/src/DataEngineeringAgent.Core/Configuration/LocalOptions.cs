using System.ComponentModel.DataAnnotations;

namespace DataEngineeringAgent.Core.Configuration;

public class LocalOptions
{
    public const string SectionName = "Local";

    [Required]
    public string DataRoot { get; set; } = string.Empty;

    [Required]
    public string OutputRoot { get; set; } = string.Empty;

    [Required]
    public string ChatRoot { get; set; } = string.Empty;

    [Required]
    public string ApprovedCodeRoot { get; set; } = string.Empty;

    [Required]
    public string SparkSubmitPath { get; set; } = string.Empty;

    public string SparkHome { get; set; } = string.Empty;

    public string PythonPath { get; set; } = string.Empty;

    public string HadoopHome { get; set; } = string.Empty;
}
