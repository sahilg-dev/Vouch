using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobCopilot.Api.OpenAI;

public class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4.1-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/responses";
}

/// <summary>
/// Thin wrapper over the OpenAI Responses API. Exposes a single text-completion
/// call plus a JSON-constrained variant used by the parsing/matching/tailoring
/// services. Deliberately uses raw HttpClient so there is no hidden SDK behavior.
/// </summary>
public class OpenAiClient(HttpClient http, OpenAiOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> CompleteAsync(
        string system, string userPrompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new OpenAiException("OPENAI_API_KEY is missing. Add it to backend/.env or your environment variables.");

        var payload = new
        {
            model = options.Model,
            instructions = system,
            input = userPrompt,
            max_output_tokens = maxTokens,
            temperature = 0.2
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new OpenAiException($"OpenAI API {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var text = ExtractText(doc.RootElement);
        if (string.IsNullOrWhiteSpace(text))
            throw new OpenAiException($"OpenAI returned no text. Raw: {body}");

        return text;
    }

    /// <summary>
    /// Runs a completion and parses the result as JSON of type T. Strips Markdown
    /// code fences defensively and throws a clear error if the model did not comply.
    /// </summary>
    public async Task<T> CompleteJsonAsync<T>(
        string system, string userPrompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        var raw = await CompleteAsync(
            system + "\n\nRespond with ONLY valid JSON. No prose, no Markdown, no code fences.",
            userPrompt, maxTokens, ct);

        var cleaned = StripFences(raw);
        try
        {
            return JsonSerializer.Deserialize<T>(cleaned, Json)
                   ?? throw new OpenAiException("Model returned null JSON.");
        }
        catch (JsonException ex)
        {
            throw new OpenAiException($"Could not parse model JSON: {ex.Message}\nRaw: {cleaned}");
        }
    }

    private static string ExtractText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString() ?? "";

        var sb = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString());
                }
            }
        }
        return sb.ToString();
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0) s = s[(firstNewline + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }
}

public class OpenAiException(string message) : Exception(message);
