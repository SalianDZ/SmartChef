using SmartChefAI.Services.Models;

namespace SmartChefAI.ViewModels.Meals;

public class MealGenerationResultViewModel
{
    public MealGenerationRequestViewModel Request { get; set; } = new();

    public GeneratedMeal Meal { get; set; } = new();

    public string MealJson { get; set; } = string.Empty;
}
