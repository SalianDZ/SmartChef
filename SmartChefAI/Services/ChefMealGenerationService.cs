using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartChefAI.Models;
using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public class ChefMealGenerationService : IMealGenerationService
{
    private readonly INutritionService _nutritionService;
    private readonly IAiTextService _aiTextService;
    private readonly ILogger<ChefMealGenerationService> _logger;

    public ChefMealGenerationService(
        INutritionService nutritionService,
        IAiTextService aiTextService,
        ILogger<ChefMealGenerationService> logger)
    {
        _nutritionService = nutritionService;
        _aiTextService = aiTextService;
        _logger = logger;
    }

    public async Task<GeneratedMeal> GenerateMealAsync(MealGenerationRequestViewModel request, CancellationToken cancellationToken = default)
    {
        var (nutritionSummary, ingredientNutrition) =
            await _nutritionService.GetNutritionAsync(request, cancellationToken).ConfigureAwait(false);

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

        var aiIdea = await _aiTextService.GenerateMealIdeaAsync(request, nutritionSummary, cancellationToken)
            .ConfigureAwait(false);

        if (aiIdea != null)
        {
            if (!string.IsNullOrWhiteSpace(aiIdea.Title))
            {
                meal.Title = aiIdea.Title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(aiIdea.Description))
            {
                meal.Description = aiIdea.Description.Trim();
            }

            if (aiIdea.Instructions?.Any() == true)
            {
                meal.Instructions = aiIdea.Instructions
                    .Where(step => !string.IsNullOrWhiteSpace(step))
                    .Select((text, index) => new MealInstruction
                    {
                        StepNumber = index + 1,
                        Text = text.Trim()
                    })
                    .ToList();
            }

            if (aiIdea.Ingredients?.Any() == true)
            {
                meal.Ingredients = aiIdea.Ingredients
                    .Where(ingredient => !string.IsNullOrWhiteSpace(ingredient.Name))
                    .Select(aiIngredient => new MealIngredient
                    {
                        Name = aiIngredient.Name,
                        Amount = aiIngredient.Amount,
                        Unit = aiIngredient.Unit,
                        Calories = null
                    })
                    .ToList();
            }
        }

        _logger.LogInformation(
            "Generated Chef AI meal {Title} with {IngredientCount} ingredients and {Calories} calories.",
            meal.Title,
            meal.Ingredients.Count,
            meal.Nutrition.TotalCalories);

        return meal;
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
        scalingFactor = Math.Clamp(scalingFactor, 0.6m, 1.4m);

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
        var suffix = request.Ingredients.Count switch
        {
            <= 2 => "Gourmet Plate",
            <= 4 => "Chef Crafted Bowl",
            _ => "Signature Tasting"
        };

        return $"{primaryIngredient} {suffix}";
    }

    private static string BuildDescription(MealGenerationRequestViewModel request, NutritionSummary summary)
    {
        var sb = new StringBuilder();
        sb.Append("ChefAI curated meal balancing your inputs with smart nutrition insights");

        if (request.CalorieTarget > 0)
        {
            var diff = summary.TotalCalories - request.CalorieTarget;
            var direction = diff switch
            {
                > 75 => "with extra energy for your goal",
                < -75 => "with a lighter finish than requested",
                _ => "aligned closely with your calorie target"
            };

            sb.Append($", {direction}.");
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

    private static List<MealInstruction> BuildInstructions(MealGenerationRequestViewModel request)
    {
        var steps = new List<string>
        {
            "Prep all fresh ingredients carefully, focusing on even cuts for consistent cooking.",
            $"Sear or cook the hero ingredient ({request.Ingredients.First().Name}) to capture flavor.",
            "Layer supporting ingredients, starting with aromatics and finishing with delicate items.",
            "Deglaze or moisten the pan as needed, tasting and adjusting seasoning thoughtfully.",
            "Plate with intention: balance textures, add a finishing drizzle, and garnish for color."
        };

        return steps.Select((text, idx) => new MealInstruction
        {
            StepNumber = idx + 1,
            Text = text
        }).ToList();
    }
}
