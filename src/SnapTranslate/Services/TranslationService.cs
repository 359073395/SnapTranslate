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
        string? apiKey = Environment.GetEnvironmentVariable("SNAPTRANSLATE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "请先设置环境变量 SNAPTRANSLATE_API_KEY，再使用 OpenAI 兼容接口。");
        }

        if (!Uri.TryCreate(_settings.OpenAiEndpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException("OpenAI 兼容接口地址无效。");
        }

        string model = string.IsNullOrWhiteSpace(_settings.OpenAiModel)
            ? "gpt-4.1-mini"
            : _settings.OpenAiModel;

        string payload = JsonSerializer.Serialize(new
        {
            model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        $"Translate the user text into {_settings.TargetLanguage}. Return only the translation and preserve line breaks."
                },
                new
                {
                    role = "user",
                    content = text
                }
            }
        });

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement content))
        {
            string result = content.GetString()?.Trim() ?? string.Empty;
            if (result.Length > 0)
            {
                return result;
            }
        }

        throw new InvalidOperationException("OpenAI 兼容接口返回格式无法识别。");
    }
}
