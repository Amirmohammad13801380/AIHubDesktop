namespace AIHubDesktop.Models;

public enum ApiProtocol
{
    OpenAiCompatible,
    Gemini,
    Ollama
}

public sealed record ProviderDefinition(
    string Id,
    string Name,
    ApiProtocol Protocol,
    string DefaultBaseUrl,
    bool RequiresApiKey,
    bool FilterZeroPriceModels = false,
    string? ApiKeyPage = null);

public sealed record ChatMessage(
    string Role,
    string Content);

public sealed record AiModel(
    string Id,
    string Name,
    string? Description = null,
    bool IsFree = false)
{
    public override string ToString()
    {
        return IsFree ? $"{Name} — رایگان" : Name;
    }
}

public sealed class StoredSettings
{
    public string LastProviderId { get; set; } = "ollama";

    public string LastModel { get; set; } = string.Empty;

    public Dictionary<string, string> EncryptedApiKeys { get; set; } = new();

    public Dictionary<string, string> BaseUrls { get; set; } = new();
}
