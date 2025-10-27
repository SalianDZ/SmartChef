using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public interface INutritionService
{
    Task<(NutritionSummary Summary, List<IngredientNutrition> Ingredients)> GetNutritionAsync(
        MealGenerationRequestViewModel request,
        CancellationToken cancellationToken = default);
}
