using System.Text;
using Microsoft.Extensions.Logging;
using SmartChefAI.Models;
using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class MockMealGenerationService : IMealGenerationService
{
    private readonly INutritionService _nutritionService;
    private readonly ILogger<MockMealGenerationService> _logger;
    private readonly Random _random = new();

    public MockMealGenerationService(INutritionService nutritionService, ILogger<MockMealGenerationService> logger)
    {
        _nutritionService = nutritionService;
        _logger = logger;
    }

    public async Task<GeneratedMeal> GenerateMealAsync(
        MealGenerationRequestViewModel request,
        CancellationToken cancellationToken = default)
    {
        var (nutritionSummary, ingredientNutrition) =
            await _nutritionService.GetNutritionAsync(request, cancellationToken).ConfigureAwait(false);

        EnsureSupportingIngredients(request, nutritionSummary, ingredientNutrition);

        AdjustNutritionToTarget(request, nutritionSummary, ingredientNutrition);

        var meal = new GeneratedMeal
        {
            Title = BuildTitle(request),
            Description = BuildDescription(request, nutritionSummary),
            InputSummary = BuildInputSummary(request),
            Nutrition = nutritionSummary,
            Ingredients = BuildMealIngredients(request, ingredientNutrition),
            Instructions = BuildInstructions(request)
        };

        _logger.LogInformation(
            "Generated mock meal {Title} with {IngredientCount} ingredients and {Calories} calories.",
            meal.Title,
            meal.Ingredients.Count,
            meal.Nutrition.TotalCalories);

        return meal;
    }

    private void EnsureSupportingIngredients(
        MealGenerationRequestViewModel request,
        NutritionSummary summary,
        List<IngredientNutrition> ingredientNutrition)
    {
        if (request.Ingredients.Count > 1)
        {
            return;
        }

        var complementSets = new[]
        {
            new[]
            {
                ("Herbed Quinoa (AI addition)", 1m, "cup cooked", 215m, 8m, 39m, 3m),
                ("Roasted Seasonal Vegetables (AI addition)", 1m, "cup", 85m, 3m, 14m, 3m)
            },
            new[]
            {
                ("Garlic Brown Rice (AI addition)", 0.75m, "cup cooked", 170m, 4m, 35m, 2m),
                ("Steamed Broccoli & Carrots (AI addition)", 1m, "cup", 55m, 3m, 11m, 1m)
            },
            new[]
            {
                ("Wholegrain Pasta (AI addition)", 1m, "cup cooked", 210m, 7m, 40m, 3m),
                ("Mixed Leaf Salad with Olive Oil (AI addition)", 1m, "cup", 95m, 2m, 7m, 6m)
            }
        };

        var selectedSet = complementSets[_random.Next(complementSets.Length)];

        foreach (var complement in selectedSet)
        {
            var (name, quantity, unit, calories, protein, carbs, fat) = complement;

            request.Ingredients.Add(new IngredientInputModel
            {
                Name = name,
                Quantity = quantity,
                Unit = unit
            });

            var nutrition = new IngredientNutrition
            {
                Name = name,
                Calories = calories,
                ProteinGrams = protein,
                CarbohydrateGrams = carbs,
                FatGrams = fat
            };

            ingredientNutrition.Add(nutrition);

            summary.TotalCalories += nutrition.Calories;
            summary.TotalProteinGrams += nutrition.ProteinGrams;
            summary.TotalCarbohydrateGrams += nutrition.CarbohydrateGrams;
            summary.TotalFatGrams += nutrition.FatGrams;
        }
    }

    private void AdjustNutritionToTarget(
        MealGenerationRequestViewModel request,
        NutritionSummary summary,
        List<IngredientNutrition> ingredientNutrition)
    {
        if (request.CalorieTarget <= 0 || summary.TotalCalories <= 0)
        {
            return;
        }

        var scalingFactor = request.CalorieTarget / summary.TotalCalories;
        scalingFactor = Math.Clamp(scalingFactor, 0.5m, 1.5m);

        summary.TotalCalories = Math.Round(summary.TotalCalories * scalingFactor, 2);
        summary.TotalProteinGrams = Math.Round(summary.TotalProteinGrams * scalingFactor, 2);
        summary.TotalCarbohydrateGrams = Math.Round(summary.TotalCarbohydrateGrams * scalingFactor, 2);
        summary.TotalFatGrams = Math.Round(summary.TotalFatGrams * scalingFactor, 2);

        foreach (var ingredient in ingredientNutrition)
        {
            ingredient.Calories = Math.Round(ingredient.Calories * scalingFactor, 2);
            ingredient.ProteinGrams = Math.Round(ingredient.ProteinGrams * scalingFactor, 2);
            ingredient.CarbohydrateGrams = Math.Round(ingredient.CarbohydrateGrams * scalingFactor, 2);
            ingredient.FatGrams = Math.Round(ingredient.FatGrams * scalingFactor, 2);
        }
    }

    private string BuildTitle(MealGenerationRequestViewModel request)
    {
        var primaryIngredient = request.Ingredients.First().Name;
        var style = _random.Next(0, 3) switch
        {
            0 => "Power Bowl",
            1 => "Balanced Plate",
            _ => "Chef's Special"
        };

        return $"{primaryIngredient} {style}";
    }

    private static string BuildDescription(MealGenerationRequestViewModel request, NutritionSummary summary)
    {
        var sb = new StringBuilder();
        sb.Append("A wholesome meal tailored to your inputs");

        if (request.CalorieTarget > 0)
        {
            var diff = summary.TotalCalories - request.CalorieTarget;
            var direction = diff switch
            {
                > 50 => "slightly above",
                < -50 => "slightly below",
                _ => "aligned with"
            };

            sb.Append($" and {direction} your calorie target.");
        }
        else
        {
            sb.Append(".");
        }

        return sb.ToString();
    }

    private static string BuildInputSummary(MealGenerationRequestViewModel request)
    {
        return string.Join(", ", request.Ingredients.Select(i =>
        {
            if (i.Quantity.HasValue && !string.IsNullOrWhiteSpace(i.Unit))
            {
                return $"{i.Quantity} {i.Unit} {i.Name}";
            }

            if (i.Quantity.HasValue)
            {
                return $"{i.Quantity} {i.Name}";
            }

            return i.Name;
        }));
    }

    private static List<MealIngredient> BuildMealIngredients(
        MealGenerationRequestViewModel request,
        List<IngredientNutrition> ingredientNutrition)
    {
        var list = new List<MealIngredient>();
        for (var index = 0; index < request.Ingredients.Count; index++)
        {
            var ingredientInput = request.Ingredients[index];
            var nutrition = ingredientNutrition.ElementAtOrDefault(index);

            list.Add(new MealIngredient
            {
                Name = ingredientInput.Name,
                Amount = ingredientInput.Quantity,
                Unit = ingredientInput.Unit,
                Calories = Math.Round(nutrition?.Calories ?? 0, 2)
            });
        }

        return list;
    }

    private List<MealInstruction> BuildInstructions(MealGenerationRequestViewModel request)
    {
        var steps = new List<string>
        {
            "Prep all ingredients by washing, chopping, or measuring as needed.",
            $"Heat a pan over medium heat and start with the base ingredient: {request.Ingredients.First().Name}.",
            "Combine remaining ingredients gradually, adjusting seasoning to taste.",
            "Simmer until flavors meld and textures reach your preference.",
            "Serve warm and garnish with fresh herbs or a squeeze of citrus."
        };

        if (request.Ingredients.Count > 3)
        {
            steps.Insert(2, "Layer in supporting ingredients to build complexity.");
        }

        return steps.Select((text, idx) => new MealInstruction
        {
            StepNumber = idx + 1,
            Text = text
        }).ToList();
    }
}
