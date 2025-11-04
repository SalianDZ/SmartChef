using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartChefAI.Services.Models;
using SmartChefAI.Services.Options;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class GeminiAiTextService : IAiTextService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiApiOptions _options;
    private readonly ILogger<GeminiAiTextService> _logger;

    public GeminiAiTextService(HttpClient httpClient, IOptions<GeminiApiOptions> options, ILogger<GeminiAiTextService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiMealIdea?> GenerateMealIdeaAsync(MealGenerationRequestViewModel request, NutritionSummary nutrition, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini API key not configured; skipping AI generation.");
            return null;
        }

        try
        {
            var prompt = BuildPrompt(request, nutrition);
            var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-1.5-flash" : _options.Model;
            var relativeUrl = $"v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.8,
                    topP = 0.9,
                    maxOutputTokens = 600
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(relativeUrl, requestBody, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Gemini API returned {StatusCode}: {Body}", response.StatusCode, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseResponse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini AI request failed.");
            return null;
        }
    }

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKeyEnvironmentVariable))
        {
            return Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        }

        return null;
    }

    private string BuildPrompt(MealGenerationRequestViewModel request, NutritionSummary nutrition)
    {
        var ingredients = string.Join("; ", request.Ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i =>
            {
                var parts = new List<string> { i.Name };
                if (i.Quantity.HasValue)
                {
                    parts.Add(i.Quantity.Value.ToString("0.##"));
                }
                if (!string.IsNullOrWhiteSpace(i.Unit))
                {
                    parts.Add(i.Unit!);
                }
                return string.Join(" ", parts);
            }));

        var target = request.CalorieTarget > 0
            ? $"Target calories: {request.CalorieTarget} kcal."
            : "No explicit calorie target provided.";

        var macros =
            $"Approximate macros (per meal): calories={nutrition.TotalCalories:0.##}, protein={nutrition.TotalProteinGrams:0.##}g, carbs={nutrition.TotalCarbohydrateGrams:0.##}g, fat={nutrition.TotalFatGrams:0.##}g.";

        var systemPrompt = string.IsNullOrWhiteSpace(_options.DefaultSystemPrompt)
            ? "You are ChefAI, an assistant that creates healthy, flavorful meals. Respond with concise JSON."
            : _options.DefaultSystemPrompt;

        return $@"{systemPrompt}

Ingredients: {ingredients}
{target}
{macros}

Return a JSON object with the following shape:
{{
  ""title"": string,
  ""description"": string,
  ""instructions"": [string, string, ...] // 4-6 clear cooking steps
}}

Instructions must reference the provided ingredients and promote balanced nutrition. Do not include markdown or text outside the JSON object.";
    }

    private static AiMealIdea? ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var firstCandidate = candidates.EnumerateArray().FirstOrDefault();
        if (firstCandidate.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = parts.EnumerateArray()
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(text);
            var payloadRoot = payload.RootElement;

            var idea = new AiMealIdea
            {
                Title = payloadRoot.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                Description = payloadRoot.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                Instructions = payloadRoot.TryGetProperty("instructions", out var instructionsElement) && instructionsElement.ValueKind == JsonValueKind.Array
                    ? instructionsElement.EnumerateArray()
                        .Select(i => i.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .ToList()!
                    : new List<string>()
            };

            return idea;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
