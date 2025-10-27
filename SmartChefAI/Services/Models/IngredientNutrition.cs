namespace SmartChefAI.Services.Models;

public class IngredientNutrition
{
    public string Name { get; set; } = string.Empty;

    public decimal Calories { get; set; }

    public decimal ProteinGrams { get; set; }

    public decimal CarbohydrateGrams { get; set; }

    public decimal FatGrams { get; set; }
}
