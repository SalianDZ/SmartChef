using System.Threading;
using System.Threading.Tasks;
using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Services;

public interface IAiTextService
{
    Task<AiMealIdea?> GenerateMealIdeaAsync(
        MealGenerationRequestViewModel request,
        NutritionSummary nutrition,
        CancellationToken cancellationToken = default);
}
