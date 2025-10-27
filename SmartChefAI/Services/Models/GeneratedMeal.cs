using System.Collections.Generic;
using SmartChefAI.Models;

namespace SmartChefAI.Services.Models;

public class GeneratedMeal
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string InputSummary { get; set; } = string.Empty;

    public NutritionSummary Nutrition { get; set; } = new();

    public List<MealIngredient> Ingredients { get; set; } = new();

    public List<MealInstruction> Instructions { get; set; } = new();
}
