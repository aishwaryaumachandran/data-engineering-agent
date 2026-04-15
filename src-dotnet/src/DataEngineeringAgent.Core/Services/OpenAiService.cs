using System.Text.Json;
using Azure.AI.OpenAI;
using DataEngineeringAgent.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace DataEngineeringAgent.Core.Services;

public class OpenAiService : IOpenAiService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiService> _logger;

    public OpenAiService(AzureOpenAIClient client, IOptions<OpenAiOptions> options, ILogger<OpenAiService> logger)
    {
        _options = options.Value;
        _chatClient = client.GetChatClient(_options.DeploymentName);
        _logger = logger;
    }

    public async Task<string> RunAgentAsync(string systemPrompt, string userMessage)
    {
        _logger.LogInformation("Calling Azure OpenAI (deployment={Deployment})", _options.DeploymentName);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage),
        };

        var completionOptions = new ChatCompletionOptions
        {
            Temperature = _options.Temperature,
        };

        var response = await _chatClient.CompleteChatAsync(messages, completionOptions);
        var result = response.Value.Content[0].Text;

        _logger.LogInformation(
            "LLM response: {Chars} chars, {PromptTokens} prompt tokens, {CompletionTokens} completion tokens",
            result.Length,
            response.Value.Usage.InputTokenCount,
            response.Value.Usage.OutputTokenCount);

        return result;
    }

    public async Task<T> RunAgentJsonAsync<T>(string systemPrompt, string userMessage)
    {
        var result = await RunAgentAsync(systemPrompt, userMessage);
        var text = result.Trim();

        // Strip markdown code fences if present
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n').ToList();
            lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim() == "```")
                lines.RemoveAt(lines.Count - 1);
            text = string.Join('\n', lines);
        }

        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }) ?? throw new InvalidOperationException("Failed to deserialize LLM response as JSON");
    }

    public async Task<string> RunAgentCodeAsync(string systemPrompt, string userMessage)
    {
        var result = await RunAgentAsync(systemPrompt, userMessage);
        var text = result.Trim();

        // Strip markdown code fences if present
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n').ToList();
            lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim() == "```")
                lines.RemoveAt(lines.Count - 1);
            text = string.Join('\n', lines).Trim();
        }

        return text;
    }
}
