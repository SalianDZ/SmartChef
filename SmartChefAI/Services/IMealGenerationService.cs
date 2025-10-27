using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public interface IMealGenerationService
{
    Task<GeneratedMeal> GenerateMealAsync(
        MealGenerationRequestViewModel request,
        CancellationToken cancellationToken = default);
}
