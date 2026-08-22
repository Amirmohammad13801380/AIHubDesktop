using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIHubDesktop.Models;

namespace AIHubDesktop.Services;

public sealed class AiApiClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AiApiClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "AIHubDesktop/1.0");
    }

    public async Task<IReadOnlyList<AiModel>> LoadModelsAsync(
        ProviderDefinition provider,
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        return provider.Protocol switch
        {
            ApiProtocol.Ollama =>
                await LoadOllamaModelsAsync(
                    baseUrl,
                    cancellationToken),

            ApiProtocol.Gemini =>
                await LoadGeminiModelsAsync(
                    baseUrl,
                    apiKey,
                    cancellationToken),

            _ =>
                await LoadOpenAiModelsAsync(
                    provider,
                    baseUrl,
                    apiKey,
                    cancellationToken)
        };
    }

    private async Task<IReadOnlyList<AiModel>> LoadOpenAiModelsAsync(
        ProviderDefinition provider,
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        string url = $"{baseUrl.TrimEnd('/')}/models";

        if (provider.Id == "openrouter")
        {
            url += "?limit=1000";
        }

        using HttpRequestMessage request =
            new(HttpMethod.Get, url);

        AddAuthorization(request, apiKey);

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);

        List<AiModel> result = new();

        if (!document.RootElement.TryGetProperty(
                "data",
                out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return GetFallbackModels(provider.Id);
        }

        foreach (JsonElement item in data.EnumerateArray())
        {
            string id = GetString(item, "id");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string name = GetString(item, "name");

            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            string description = GetString(item, "description");

            bool isFree =
                id.EndsWith(":free",
                    StringComparison.OrdinalIgnoreCase);

            if (item.TryGetProperty(
                    "pricing",
                    out JsonElement pricing))
            {
                decimal promptPrice =
                    ReadDecimal(pricing, "prompt");

                decimal completionPrice =
                    ReadDecimal(pricing, "completion");

                if (promptPrice == 0 && completionPrice == 0)
                {
                    isFree = true;
                }
            }

            if (provider.FilterZeroPriceModels && !isFree)
            {
                continue;
            }

            result.Add(new AiModel(
                id,
                name,
                description,
                isFree));
        }

        if (provider.Id == "openrouter" &&
            result.All(x => x.Id != "openrouter/free"))
        {
            result.Insert(
                0,
                new AiModel(
                    "openrouter/free",
                    "OpenRouter Free Models Router",
                    "انتخاب خودکار یک مدل رایگان",
                    true));
        }

        return result
            .OrderByDescending(x => x.IsFree)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private async Task<IReadOnlyList<AiModel>> LoadGeminiModelsAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "برای Gemini باید API Key وارد شود.");
        }

        string url =
            $"{baseUrl.TrimEnd('/')}/models" +
            $"?pageSize=1000&key={Uri.EscapeDataString(apiKey)}";

        using HttpResponseMessage response =
            await _httpClient.GetAsync(url, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);

        List<AiModel> result = new();

        if (!document.RootElement.TryGetProperty(
                "models",
                out JsonElement models))
        {
            return result;
        }

        foreach (JsonElement model in models.EnumerateArray())
        {
            bool supportsGenerateContent = false;

            if (model.TryGetProperty(
                    "supportedGenerationMethods",
                    out JsonElement methods))
            {
                supportsGenerateContent = methods
                    .EnumerateArray()
                    .Any(x =>
                        string.Equals(
                            x.GetString(),
                            "generateContent",
                            StringComparison.OrdinalIgnoreCase));
            }

            if (!supportsGenerateContent)
            {
                continue;
            }

            string resourceName = GetString(model, "name");

            string id = GetString(model, "baseModelId");

            if (string.IsNullOrWhiteSpace(id))
            {
                id = resourceName.Replace("models/", "");
            }

            string name = GetString(model, "displayName");

            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            result.Add(new AiModel(
                id,
                name,
                GetString(model, "description"),
                true));
        }

        return result
            .OrderByDescending(x =>
                x.Id.Contains(
                    "flash",
                    StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name)
            .ToList();
    }

    private async Task<IReadOnlyList<AiModel>> LoadOllamaModelsAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        string root = NormalizeOllamaRoot(baseUrl);

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                $"{root}/api/tags",
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);

        List<AiModel> result = new();

        if (!document.RootElement.TryGetProperty(
                "models",
                out JsonElement models))
        {
            return result;
        }

        foreach (JsonElement model in models.EnumerateArray())
        {
            string id = GetString(model, "name");

            if (string.IsNullOrWhiteSpace(id))
            {
                id = GetString(model, "model");
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(new AiModel(
                    id,
                    id,
                    "مدل محلی Ollama",
                    true));
            }
        }

        return result.OrderBy(x => x.Name).ToList();
    }

    public async Task StreamChatAsync(
        ProviderDefinition provider,
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<ChatMessage> messages,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        switch (provider.Protocol)
        {
            case ApiProtocol.Ollama:
                await StreamOllamaAsync(
                    baseUrl,
                    model,
                    messages,
                    onChunk,
                    cancellationToken);
                break;

            case ApiProtocol.Gemini:
                await StreamGeminiAsync(
                    baseUrl,
                    apiKey,
                    model,
                    messages,
                    onChunk,
                    cancellationToken);
                break;

            default:
                await StreamOpenAiAsync(
                    provider,
                    baseUrl,
                    apiKey,
                    model,
                    messages,
                    onChunk,
                    cancellationToken);
                break;
        }
    }

    private async Task StreamOpenAiAsync(
        ProviderDefinition provider,
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<ChatMessage> messages,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        string url =
            $"{baseUrl.TrimEnd('/')}/chat/completions";

        object payload = new
        {
            model,
            messages,
            stream = true
        };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                url,
                payload);

        AddAuthorization(request, apiKey);

        if (provider.Id == "openrouter")
        {
            request.Headers.TryAddWithoutValidation(
                "X-Title",
                "AI Hub Desktop");
        }

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using StreamReader reader = new(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line =
                await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith(
                    "data:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string data = line[5..].Trim();

            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(data);

                JsonElement root = document.RootElement;

                if (!root.TryGetProperty(
                        "choices",
                        out JsonElement choices) ||
                    choices.GetArrayLength() == 0)
                {
                    continue;
                }

                JsonElement choice = choices[0];

                if (!choice.TryGetProperty(
                        "delta",
                        out JsonElement delta))
                {
                    continue;
                }

                if (delta.TryGetProperty(
                        "content",
                        out JsonElement content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    string? text = content.GetString();

                    if (!string.IsNullOrEmpty(text))
                    {
                        onChunk(text);
                    }
                }
            }
            catch (JsonException)
            {
                // بعضی سرویس‌ها بین eventها پیام غیر JSON می‌فرستند.
            }
        }
    }

    private async Task StreamOllamaAsync(
        string baseUrl,
        string model,
        IReadOnlyList<ChatMessage> messages,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        string root = NormalizeOllamaRoot(baseUrl);

        object payload = new
        {
            model,
            messages,
            stream = true
        };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                $"{root}/api/chat",
                payload);

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using StreamReader reader = new(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line =
                await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document =
                JsonDocument.Parse(line);

            JsonElement root = document.RootElement;

            if (root.TryGetProperty(
                    "message",
                    out JsonElement message) &&
                message.TryGetProperty(
                    "content",
                    out JsonElement content))
            {
                string? text = content.GetString();

                if (!string.IsNullOrEmpty(text))
                {
                    onChunk(text);
                }
            }

            if (root.TryGetProperty(
                    "done",
                    out JsonElement done) &&
                done.ValueKind == JsonValueKind.True)
            {
                break;
            }
        }
    }

    private async Task StreamGeminiAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<ChatMessage> messages,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "برای Gemini باید API Key وارد شود.");
        }

        string url =
            $"{baseUrl.TrimEnd('/')}/models/" +
            $"{Uri.EscapeDataString(model)}:" +
            "streamGenerateContent" +
            $"?alt=sse&key={Uri.EscapeDataString(apiKey)}";

        string? systemInstruction = messages
            .LastOrDefault(x => x.Role == "system")
            ?.Content;

        var contents = messages
            .Where(x => x.Role != "system")
            .Select(x => new
            {
                role = x.Role == "assistant" ? "model" : "user",
                parts = new[]
                {
                    new { text = x.Content }
                }
            })
            .ToArray();

        object payload;

        if (string.IsNullOrWhiteSpace(systemInstruction))
        {
            payload = new
            {
                contents
            };
        }
        else
        {
            payload = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = systemInstruction }
                    }
                },
                contents
            };
        }

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                url,
                payload);

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using StreamReader reader = new(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line =
                await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line) ||
                !line.StartsWith(
                    "data:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string data = line[5..].Trim();

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(data);

                JsonElement root = document.RootElement;

                if (!root.TryGetProperty(
                        "candidates",
                        out JsonElement candidates) ||
                    candidates.GetArrayLength() == 0)
                {
                    continue;
                }

                JsonElement candidate = candidates[0];

                if (!candidate.TryGetProperty(
                        "content",
                        out JsonElement content) ||
                    !content.TryGetProperty(
                        "parts",
                        out JsonElement parts))
                {
                    continue;
                }

                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty(
                            "text",
                            out JsonElement textElement))
                    {
                        string? text = textElement.GetString();

                        if (!string.IsNullOrEmpty(text))
                        {
                            onChunk(text);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // نادیده گرفتن event ناقص
            }
        }
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string url,
        object payload)
    {
        string json =
            JsonSerializer.Serialize(payload, JsonOptions);

        return new HttpRequestMessage(method, url)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static void AddAuthorization(
        HttpRequestMessage request,
        string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey.Trim());
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (body.Length > 2500)
        {
            body = body[..2500];
        }

        throw new HttpRequestException(
            $"خطای API: {(int)response.StatusCode} " +
            $"{response.ReasonPhrase}\n{body}");
    }

    private static string NormalizeOllamaRoot(string baseUrl)
    {
        string root = baseUrl.Trim().TrimEnd('/');

        if (root.EndsWith(
                "/v1",
                StringComparison.OrdinalIgnoreCase))
        {
            root = root[..^3];
        }

        return root;
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static decimal ReadDecimal(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out decimal number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                value.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static IReadOnlyList<AiModel> GetFallbackModels(
        string providerId)
    {
        return providerId switch
        {
            "openrouter" => new[]
            {
                new AiModel(
                    "openrouter/free",
                    "OpenRouter Free Models Router",
                    "انتخاب خودکار مدل رایگان",
                    true)
            },

            "groq" => new[]
            {
                new AiModel(
                    "openai/gpt-oss-120b",
                    "GPT OSS 120B"),
                new AiModel(
                    "openai/gpt-oss-20b",
                    "GPT OSS 20B"),
                new AiModel(
                    "groq/compound",
                    "Groq Compound"),
                new AiModel(
                    "groq/compound-mini",
                    "Groq Compound Mini")
            },

            "huggingface" => new[]
            {
                new AiModel(
                    "openai/gpt-oss-120b",
                    "GPT OSS 120B"),
                new AiModel(
                    "deepseek-ai/DeepSeek-R1",
                    "DeepSeek R1"),
                new AiModel(
                    "Qwen/Qwen3-Coder-480B-A35B-Instruct",
                    "Qwen3 Coder"),
                new AiModel(
                    "zai-org/GLM-4.5",
                    "GLM 4.5")
            },

            "cerebras" => new[]
            {
                new AiModel(
                    "gpt-oss-120b",
                    "GPT OSS 120B")
            },

            "nvidia" => new[]
            {
                new AiModel(
                    "moonshotai/kimi-k2.5",
                    "Kimi K2.5"),
                new AiModel(
                    "openai/gpt-oss-120b",
                    "GPT OSS 120B")
            },

            "deepseek" => new[]
            {
                new AiModel(
                    "deepseek-chat",
                    "DeepSeek Chat"),
                new AiModel(
                    "deepseek-reasoner",
                    "DeepSeek Reasoner")
            },

            _ => Array.Empty<AiModel>()
        };
    }
}
