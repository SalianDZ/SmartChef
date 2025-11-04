using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartChefAI.Services.Models;
using SmartChefAI.Services.Options;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class RealNutritionService : INutritionService
{
    private readonly HttpClient _httpClient;
    private readonly NutritionApiOptions _options;
    private readonly ILogger<RealNutritionService> _logger;
    private readonly IAppLogService _appLogService;

    private sealed record NutritionixRequest([property: JsonPropertyName("query")] string Query);

    private sealed record NutritionixFood(
        [property: JsonPropertyName("food_name")] string FoodName,
        [property: JsonPropertyName("nf_calories")] decimal Calories,
        [property: JsonPropertyName("nf_protein")] decimal Protein,
        [property: JsonPropertyName("nf_total_carbohydrate")] decimal Carbs,
        [property: JsonPropertyName("nf_total_fat")] decimal Fat);

    private sealed record NutritionixResponse(
        [property: JsonPropertyName("foods")] List<NutritionixFood> Foods);

    public RealNutritionService(
        HttpClient httpClient,
        IOptions<NutritionApiOptions> options,
        ILogger<RealNutritionService> logger,
        IAppLogService appLogService)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _appLogService = appLogService;
    }

    public async Task<(NutritionSummary Summary, List<IngredientNutrition> Ingredients)> GetNutritionAsync(
        MealGenerationRequestViewModel request,
        CancellationToken cancellationToken = default)
    {
        var ingredientResults = new List<IngredientNutrition>();

        foreach (var ingredient in request.Ingredients)
        {
            var nutrition = await FetchNutritionAsync(ingredient, cancellationToken).ConfigureAwait(false);
            ingredientResults.Add(nutrition);
        }

        var summary = new NutritionSummary
        {
            TotalCalories = ingredientResults.Sum(i => i.Calories),
            TotalProteinGrams = ingredientResults.Sum(i => i.ProteinGrams),
            TotalCarbohydrateGrams = ingredientResults.Sum(i => i.CarbohydrateGrams),
            TotalFatGrams = ingredientResults.Sum(i => i.FatGrams)
        };

        return (summary, ingredientResults);
    }

    private async Task<IngredientNutrition> FetchNutritionAsync(IngredientInputModel ingredient, CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQuery(ingredient);
            var endpoint = _options.Endpoint ?? "natural/nutrients";

            var response = await _httpClient.PostAsJsonAsync(endpoint, new NutritionixRequest(query), cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<NutritionixResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var food = payload?.Foods?.FirstOrDefault();
            if (food == null || (food.Calories <= 0 && food.Protein <= 0 && food.Carbs <= 0 && food.Fat <= 0))
            {
                return CreateFallbackNutrition(ingredient, reason: "API returned empty or zeroed data.");
            }

            return new IngredientNutrition
            {
                Name = ingredient.Name,
                Calories = Math.Round(food.Calories, 2),
                ProteinGrams = Math.Round(food.Protein, 2),
                CarbohydrateGrams = Math.Round(food.Carbs, 2),
                FatGrams = Math.Round(food.Fat, 2)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch real nutrition data for ingredient {Ingredient}", ingredient.Name);

            if (_options.UseMockFallback)
            {
                await _appLogService.LogAsync(
                    "Warning",
                    $"Real nutrition API fallback for ingredient {ingredient.Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);

                return CreateFallbackNutrition(ingredient, reason: "Exception during API call.");
            }

            throw;
        }
    }

    private static string BuildQuery(IngredientInputModel ingredient)
    {
        var quantity = ingredient.Quantity.HasValue && ingredient.Quantity > 0 ? ingredient.Quantity.Value : 1;
        var unit = string.IsNullOrWhiteSpace(ingredient.Unit) ? "unit" : ingredient.Unit;
        return $"{quantity} {unit} {ingredient.Name}";
    }

    private static IngredientNutrition CreateFallbackNutrition(IngredientInputModel ingredient, string reason)
    {
        var baseCalories = 90m + (ComputeStableHash($"{ingredient.Name}-{reason}") % 120);
        var scale = GetQuantityScalingFactor(ingredient.Quantity);
        var calories = baseCalories * scale;

        var proteinCalories = calories * 0.3m;
        var carbCalories = calories * 0.4m;
        var fatCalories = calories * 0.3m;

        return new IngredientNutrition
        {
            Name = ingredient.Name,
            Calories = Math.Round(calories, 2),
            ProteinGrams = Math.Round(proteinCalories / 4m, 2),
            CarbohydrateGrams = Math.Round(carbCalories / 4m, 2),
            FatGrams = Math.Round(fatCalories / 9m, 2)
        };
    }

    private static decimal GetQuantityScalingFactor(decimal? quantity)
    {
        if (!quantity.HasValue || quantity <= 0)
        {
            return 1m;
        }

        return Math.Max(quantity.Value / 100m, 0.1m);
    }

    private static int ComputeStableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return Math.Abs(hash);
        }
    }
}
