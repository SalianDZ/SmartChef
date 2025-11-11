using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Google.Api.Gax.Grpc;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartChefAI.Services.Models;
using SmartChefAI.Services.Options;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class GeminiAiTextService : IAiTextService
{
    private readonly PredictionServiceClient _predictionClient;
    private readonly GeminiApiOptions _options;
    private readonly ILogger<GeminiAiTextService> _logger;
    private readonly IAppLogService _appLogService;

    public GeminiAiTextService(
        PredictionServiceClient predictionServiceClient,
        IOptions<GeminiApiOptions> options,
        ILogger<GeminiAiTextService> logger,
        IAppLogService appLogService)
    {
        _predictionClient = predictionServiceClient;
        _options = options.Value;
        _logger = logger;
        _appLogService = appLogService;
    }

    public async Task<AiMealIdea?> GenerateMealIdeaAsync(MealGenerationRequestViewModel request, NutritionSummary nutrition, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            await SafeLogAsync("Information", $"Gemini request started for {request.Ingredients.Count} ingredients.", cancellationToken);

            var (projectId, location, modelName) = ResolveModelSettings();
            var modelPath = $"projects/{projectId}/locations/{location}/publishers/google/models/{modelName}";

            var generateRequest = new GenerateContentRequest
            {
                Model = modelPath,
                GenerationConfig = new GenerationConfig
                {
                    Temperature = 0,
                    MaxOutputTokens = 4096,
                    ResponseMimeType = "application/json"
                }
            };

            generateRequest.Contents.Add(new Content
            {
                Role = "user",
                Parts = { new Part { Text = BuildPrompt(request, nutrition) } }
            });

            GenerateContentResponse response;
            try
            {
                response = await _predictionClient.GenerateContentAsync(
                    generateRequest,
                    callSettings: CallSettings.FromCancellationToken(cancellationToken)).ConfigureAwait(false);
            }
            catch (RpcException rpcEx)
            {
                throw new InvalidOperationException($"Vertex AI generation failed: {rpcEx.Status}", rpcEx);
            }

            if (response.Candidates.Count == 0)
            {
                await SafeLogAsync("Warning", "Gemini returned no candidates.", cancellationToken);
                return null;
            }

            var rawResponseJson = JsonFormatter.Default.Format(response);
                

            var (idea, cleanedPayload, rawText) = ExtractIdeaFromResponse(response);

            if (idea != null)
            {
                await SafeLogAsync("Information", $"Gemini meal idea generated: {idea.Title}", cancellationToken);
                return idea;
            }

            var snippet = cleanedPayload ?? rawText ?? "(empty)";
            await SafeLogAsync("Warning", $"Gemini payload could not be parsed. Payload: {snippet}", cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini AI request failed.");
            await SafeLogAsync("Warning", $"Gemini AI request failed: {ex.Message}", cancellationToken);
            return null;
        }
    }

    private (string ProjectId, string Location, string Model) ResolveModelSettings()
    {
        var projectId = !string.IsNullOrWhiteSpace(_options.ProjectId)
            ? _options.ProjectId
            : Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
                ?? throw new InvalidOperationException("Gemini ProjectId is not configured. Set GeminiApi:ProjectId or GOOGLE_CLOUD_PROJECT.");

        var location = string.IsNullOrWhiteSpace(_options.Location) ? "us-central1" : _options.Location;
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-2.5-pro" : _options.Model;
        return (projectId, location, model);
    }

    private string BuildPrompt(MealGenerationRequestViewModel request, NutritionSummary nutrition)
    {
        var ingredients = string.Join(", ", request.Ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i =>
            {
                var name = i.Name.Trim();
                var qty = i.Quantity.HasValue ? $"{i.Quantity:0.##}" : string.Empty;
                var unit = string.IsNullOrWhiteSpace(i.Unit) ? string.Empty : i.Unit;
                return $"{name} {qty} {unit}".Trim();
            }));

        var calorieTarget = request.CalorieTarget > 0
            ? request.CalorieTarget.ToString("0")
            : "auto";

        var macros =
            $"calories={nutrition.TotalCalories:0.##}, protein={nutrition.TotalProteinGrams:0.##}g, carbs={nutrition.TotalCarbohydrateGrams:0.##}g, fat={nutrition.TotalFatGrams:0.##}g";

        return $@"Respond with only a valid JSON object.

USER INPUT:
Ingredients: Chicken breast 150g, Quinoa 100g (cooked), Broccoli 75g

EXAMPLE:
{{
  ""title"": ""Grilled Chicken with Quinoa & Broccoli"",
  ""description"": ""A balanced, high-protein meal with grilled chicken, fluffy quinoa, and steamed broccoli."",
  ""ingredients"": [
    {{ ""name"": ""Chicken breast"", ""amount"": 150, ""unit"": ""g"" }},
    {{ ""name"": ""Quinoa"", ""amount"": 100, ""unit"": ""g cooked"" }},
    {{ ""name"": ""Broccoli florets"", ""amount"": 75, ""unit"": ""g"" }}
  ],
  ""instructions"": [
    ""Season and grill the 150g chicken breast until cooked."",
    ""Serve with 100g of cooked quinoa."",
    ""Steam the 75g of broccoli and serve alongside.""
  ],
  ""calories"": 375,
  ""macros"": {{
    ""protein"": ""45g"",
    ""carbs"": ""30g"",
    ""fat"": ""8g""
  }},
  ""notes"": ""Always include at least three complementary ingredients so the meal feels complete.""
}}

USER INPUT:
Ingredients: {ingredients}
Target calories: {calorieTarget}
Approximate macros: {macros}";
    }

    private (AiMealIdea? Idea, string? CleanedPayload, string? RawText) ExtractIdeaFromResponse(GenerateContentResponse response)
    {
        var firstCandidate = response.Candidates.FirstOrDefault();
        if (firstCandidate?.Content == null)
        {
            return (null, null, JsonFormatter.Default.Format(response));
        }

        var textPayload = firstCandidate.Content.Parts
            .Select(part => part.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (string.IsNullOrWhiteSpace(textPayload))
        {
            return (null, null, JsonFormatter.Default.Format(response));
        }

        try
        {
            var cleaned = CleanModelResponse(textPayload);
            _logger.LogDebug("Gemini cleaned payload: {Payload}", cleaned);
            using var payload = JsonDocument.Parse(cleaned);
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
                    : new List<string>(),
                Ingredients = ParseAiIngredients(payloadRoot)
            };

            return (idea, cleaned, textPayload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini payload: {Raw}", textPayload);
            return (null, null, textPayload);
        }
    }

    private static List<AiMealIngredient> ParseAiIngredients(JsonElement payloadRoot)
    {
        if (!payloadRoot.TryGetProperty("ingredients", out var ingredientsElement) ||
            ingredientsElement.ValueKind != JsonValueKind.Array)
        {
            return new List<AiMealIngredient>();
        }

        var list = new List<AiMealIngredient>();
        foreach (var ingredientElement in ingredientsElement.EnumerateArray())
        {
            var parsed = ParseAiIngredient(ingredientElement);
            if (parsed != null)
            {
                list.Add(parsed);
            }
        }

        return list;
    }

    private static AiMealIngredient? ParseAiIngredient(JsonElement element)
    {
        if (!element.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        var name = nameElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        decimal? amount = null;
        if (element.TryGetProperty("amount", out var amountElement))
        {
            amount = ReadDecimal(amountElement);
        }

        if (!amount.HasValue && element.TryGetProperty("quantity", out var quantityElement))
        {
            amount = ReadDecimal(quantityElement);
        }

        string? unit = null;
        if (element.TryGetProperty("unit", out var unitElement))
        {
            unit = unitElement.GetString();
        }
        else if (element.TryGetProperty("measure", out var measureElement))
        {
            unit = measureElement.GetString();
        }
        else if (element.TryGetProperty("amountText", out var amountTextElement))
        {
            unit = amountTextElement.GetString();
        }

        var result = new AiMealIngredient
        {
            Name = name,
            Amount = amount,
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim()
        };

        if (!result.Amount.HasValue && !string.IsNullOrWhiteSpace(result.Unit))
        {
            var (derivedAmount, remainder) = SplitCombinedQuantity(result.Unit);
            if (derivedAmount.HasValue)
            {
                result.Amount = derivedAmount;
                result.Unit = remainder;
            }
        }

        return result;
    }

    private static decimal? ReadDecimal(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.String => decimal.TryParse(
                    element.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : null,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static (decimal? Value, string? Remainder) SplitCombinedQuantity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var trimmed = text.Trim();
        var chars = trimmed.AsSpan();
        var index = 0;
        while (index < chars.Length && (char.IsDigit(chars[index]) || chars[index] is '.' or ','))
        {
            index++;
        }

        if (index == 0)
        {
            return (null, trimmed);
        }

        var numberPart = chars[..index].ToString().Replace(',', '.');
        if (!decimal.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return (null, trimmed);
        }

        var remainder = index < chars.Length ? chars[index..].ToString().Trim() : null;
        return (value, string.IsNullOrWhiteSpace(remainder) ? null : remainder);
    }

    private static string CleanModelResponse(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);

            var firstLineBreak = trimmed.IndexOfAny(new[] { '\n', '\r' });
            if (firstLineBreak >= 0)
            {
                var firstLine = trimmed[..firstLineBreak].Trim();
                if (string.Equals(firstLine, "json", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[(firstLineBreak + 1)..];
                }
            }

            var closingIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingIndex >= 0)
            {
                trimmed = trimmed[..closingIndex];
            }
        }

        return trimmed.Trim();
    }

    private async Task SafeLogAsync(string level, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _appLogService.LogAsync(level, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist log entry for message: {Message}", message);
        }
    }
}
