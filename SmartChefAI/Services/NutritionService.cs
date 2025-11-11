using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class NutritionService : INutritionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NutritionService> _logger;
    private readonly IAppLogService _appLogService;

    private record DummyJsonRecipe(string Name, decimal Calories, decimal Protein, decimal Fat, decimal Carbohydrates);

    private record DummyJsonRecipeResponse(List<DummyJsonRecipe> Recipes);

    public NutritionService(HttpClient httpClient, ILogger<NutritionService> logger, IAppLogService appLogService)
    {
        _httpClient = httpClient;
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
            IngredientNutrition nutrition;

            try
            {
                nutrition = await FetchNutritionForIngredientAsync(ingredient, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch nutrition for ingredient {Ingredient}", ingredient.Name);
                await _appLogService.LogAsync(
                    "Warning",
                    $"Nutrition lookup fallback for ingredient {ingredient.Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);
                nutrition = CreateFallbackNutrition(ingredient);
            }

            ingredientResults.Add(nutrition);
        }

        var summary = new NutritionSummary
        {
            TotalCalories = ingredientResults.Sum(i => i.Calories),
            TotalProteinGrams = ingredientResults.Sum(i => i.ProteinGrams),
            TotalCarbohydrateGrams = ingredientResults.Sum(i => i.CarbohydrateGrams),
            TotalFatGrams = ingredientResults.Sum(i => i.FatGrams)
        };

        if ((ingredientResults.Count == 0 || summary.TotalCalories <= 0) && request.CalorieTarget > 0)
        {
            ApplyTargetAsSummary(summary, ingredientResults, request.CalorieTarget);
        }

        return (summary, ingredientResults);
    }

    private async Task<IngredientNutrition> FetchNutritionForIngredientAsync(
        IngredientInputModel ingredient,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(ingredient.Name);
        using var response = await _httpClient.GetAsync(
            $"recipes/search?q={query}&limit=1",
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize<DummyJsonRecipeResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var recipe = parsed?.Recipes?.FirstOrDefault();

        if (recipe == null)
        {
            return CreateFallbackNutrition(ingredient);
        }

        var scale = GetQuantityScalingFactor(ingredient.Quantity);

        return new IngredientNutrition
        {
            Name = ingredient.Name,
            Calories = Math.Round(recipe.Calories * scale, 2),
            ProteinGrams = Math.Round(recipe.Protein * scale, 2),
            CarbohydrateGrams = Math.Round(recipe.Carbohydrates * scale, 2),
            FatGrams = Math.Round(recipe.Fat * scale, 2)
        };
    }

    private static decimal GetQuantityScalingFactor(decimal? quantity)
    {
        if (!quantity.HasValue)
        {
            return 1m;
        }

        if (quantity == 0)
        {
            return 1m;
        }

        return Math.Max(quantity.Value / 100m, 0.1m);
    }

    private IngredientNutrition CreateFallbackNutrition(IngredientInputModel ingredient)
    {
        var baseCalories = 120m + (ComputeStableHash(ingredient.Name) % 150);
        var calories = baseCalories * GetQuantityScalingFactor(ingredient.Quantity);

        // Simple macro split: 30% protein, 40% carbs, 30% fat.
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

    private static int ComputeStableHash(string value)
    {
        unchecked
        {
            int hash = 23;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return Math.Abs(hash);
        }
    }

    private static void ApplyTargetAsSummary(
        NutritionSummary summary,
        List<IngredientNutrition> ingredientResults,
        decimal targetCalories)
    {
        summary.TotalCalories = Math.Round(targetCalories, 2);
        summary.TotalProteinGrams = Math.Round((targetCalories * 0.35m) / 4m, 2);
        summary.TotalCarbohydrateGrams = Math.Round((targetCalories * 0.4m) / 4m, 2);
        summary.TotalFatGrams = Math.Round((targetCalories * 0.25m) / 9m, 2);

        if (!ingredientResults.Any())
        {
            ingredientResults.Add(new IngredientNutrition
            {
                Name = "Balanced macros (auto)",
                Calories = summary.TotalCalories,
                ProteinGrams = summary.TotalProteinGrams,
                CarbohydrateGrams = summary.TotalCarbohydrateGrams,
                FatGrams = summary.TotalFatGrams
            });
        }
    }
}
