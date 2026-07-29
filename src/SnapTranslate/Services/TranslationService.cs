using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SnapTranslate.Models;

namespace SnapTranslate.Services;

public sealed class TranslationService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private readonly AppSettings _settings;

    public TranslationService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("没有可翻译的文字。");
        }

        return string.Equals(_settings.TranslationProvider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? TranslateWithOpenAiAsync(text, cancellationToken)
            : TranslateWithGoogleWebAsync(text, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> TranslateLinesAsync(
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        string combined = string.Join('\n', lines);
        string combinedTranslation = await TranslateAsync(combined, cancellationToken);
        string[] translatedLines = combinedTranslation
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .ToArray();

        if (translatedLines.Length == lines.Count &&
            translatedLines.All(line => line.Length > 0))
        {
            return translatedLines;
        }

        string[] fallbackTranslations = new string[lines.Count];
        using SemaphoreSlim concurrency = new(3);
        IEnumerable<Task> translationTasks = lines.Select(
            async (line, index) =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    fallbackTranslations[index] =
                        await TranslateAsync(line, cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            });

        await Task.WhenAll(translationTasks);
        return fallbackTranslations;
    }

    public Task<string> TestOpenAiConnectionAsync(
        CancellationToken cancellationToken = default) =>
        TranslateWithOpenAiAsync("Hello", cancellationToken);

    private async Task<string> TranslateWithGoogleWebAsync(
        string text,
        CancellationToken cancellationToken)
    {
        string endpoint =
            $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(_settings.TargetLanguage)}&dt=t";

        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("q", text)
        ]);

        using HttpResponseMessage response = await Http.PostAsync(endpoint, content, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement segments = document.RootElement[0];
        StringBuilder translation = new();

        foreach (JsonElement segment in segments.EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Array &&
                segment.GetArrayLength() > 0 &&
                segment[0].ValueKind == JsonValueKind.String)
            {
                translation.Append(segment[0].GetString());
            }
        }

        string result = translation.ToString().Trim();
        return result.Length > 0
            ? result
            : throw new InvalidOperationException("翻译服务没有返回文字。");
    }

    private async Task<string> TranslateWithOpenAiAsync(
        string text,
        CancellationToken cancellationToken)
    {
        string? apiKey = _settings.OpenAiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable("SNAPTRANSLATE_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "请填写 API Key，再使用 OpenAI 兼容接口。");
        }

        Uri endpoint = ResolveOpenAiEndpoint(_settings.OpenAiEndpoint);

        string model = string.IsNullOrWhiteSpace(_settings.OpenAiModel)
            ? "gpt-4.1-mini"
            : _settings.OpenAiModel;

        Dictionary<string, object> payloadValues = new()
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new
                {
                    role = "system",
                    content = GetOpenAiTranslationInstruction()
                },
                new
                {
                    role = "user",
                    content = text
                }
            }
        };

        if (model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
        {
            payloadValues["reasoning_effort"] = "none";
        }
        else
        {
            payloadValues["temperature"] = 0.1;
        }

        string payload = JsonSerializer.Serialize(payloadValues);

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI 兼容接口请求失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(json)}");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement content))
        {
            string result = ExtractMessageContent(content);
            if (result.Length > 0)
            {
                return result;
            }
        }

        throw new InvalidOperationException("OpenAI 兼容接口返回格式无法识别。");
    }

    public static Uri ResolveOpenAiEndpoint(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("OpenAI 兼容接口地址无效。");
        }

        UriBuilder builder = new(endpoint);
        string path = builder.Path.TrimEnd('/');

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return builder.Uri;
        }

        builder.Path = path.Length == 0
            ? "/v1/chat/completions"
            : $"{path}/chat/completions";
        return builder.Uri;
    }

    private static string ExtractMessageContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString()?.Trim() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        StringBuilder result = new();
        foreach (JsonElement part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out JsonElement text) &&
                text.ValueKind == JsonValueKind.String)
            {
                result.Append(text.GetString());
            }
        }

        return result.ToString().Trim();
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out JsonElement nestedMessage) &&
                    nestedMessage.ValueKind == JsonValueKind.String)
                {
                    return LimitErrorLength(nestedMessage.GetString());
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return LimitErrorLength(error.GetString());
                }
            }

            if (root.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return LimitErrorLength(message.GetString());
            }
        }
        catch (JsonException)
        {
            // Fall back to a shortened response body below.
        }

        return LimitErrorLength(json);
    }

    private static string LimitErrorLength(string? message)
    {
        string value = string.IsNullOrWhiteSpace(message)
            ? "服务未返回错误详情。"
            : message.Trim();
        return value.Length <= 500 ? value : $"{value[..500]}…";
    }

    private string GetOpenAiTranslationInstruction()
    {
        if (string.Equals(_settings.TargetLanguage, "id", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   Translate the user text into natural Indonesian (Bahasa Indonesia) suitable for TikTok.
                   Keep it concise, contemporary, and easy to read in an image overlay.
                   Preserve line breaks, emojis, @handles, hashtags, names, numbers, and product terms.
                   Return only the translation with no explanation.
                   """;
        }

        return
            $"Translate the user text into {_settings.TargetLanguage}. " +
            "Keep it concise for an image overlay, preserve line breaks, and return only the translation.";
    }
}
