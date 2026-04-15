using System.Text.Json;
using DataEngineeringAgent.Core.Configuration;
using DataEngineeringAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataEngineeringAgent.Core.Services;

public class LocalCosmosService : ICosmosService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly string _chatRoot;
    private readonly ILogger<LocalCosmosService> _logger;

    public LocalCosmosService(IOptions<LocalOptions> opts, ILogger<LocalCosmosService> logger)
    {
        _chatRoot = opts.Value.ChatRoot;
        Directory.CreateDirectory(_chatRoot);
        _logger = logger;
    }

    public async Task SaveMessageAsync(ConversationMessage message)
    {
        var filePath = GetThreadFilePath(message.ThreadId);
        var conversation = await LoadConversationAsync(filePath);

        conversation.Add(new Dictionary<string, object?>
        {
            ["id"] = message.Id,
            ["thread_id"] = message.ThreadId,
            ["client_id"] = message.ClientId,
            ["role"] = message.Role,
            ["content"] = message.Content,
            ["phase"] = message.Phase,
            ["timestamp"] = message.Timestamp.ToString("o"),
        });

        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(conversation, JsonOptions));
        _logger.LogDebug("Saved message {Id} to local thread {ThreadId}", message.Id, message.ThreadId);
    }

    public async Task<List<JsonElement>> GetConversationHistoryAsync(string threadId)
    {
        var filePath = GetThreadFilePath(threadId);
        if (!File.Exists(filePath))
            return [];

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.EnumerateArray()
            .Select(e => e.Clone())
            .OrderBy(e => e.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : "")
            .ToList();
    }

    private string GetThreadFilePath(string threadId) =>
        Path.Combine(_chatRoot, $"{threadId}.json");

    private static async Task<List<Dictionary<string, object?>>> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json, JsonOptions) ?? [];
    }
}
