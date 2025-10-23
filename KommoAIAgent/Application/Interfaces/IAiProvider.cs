using OpenAI.Chat;

public interface IAiProvider
{
    string ProviderName { get; }

    Task<string> GenerateResponseAsync(
        List<ChatMessage> messages,
        AiRequestOptions options,
        CancellationToken ct = default
    );

    Task<string> GenerateWithImageAsync(
        List<ChatMessage> messages,
        byte[] imageBytes,
        string mimeType,
        AiRequestOptions options,
        CancellationToken ct = default
    );

    bool SupportsVision { get; }
    bool SupportsStreaming { get; }
}

public class AiRequestOptions
{
    public int MaxTokens { get; set; } = 400;
    public float Temperature { get; set; } = 0.7f;
    public string? Model { get; set; }
    public bool Stream { get; set; } = false;
}