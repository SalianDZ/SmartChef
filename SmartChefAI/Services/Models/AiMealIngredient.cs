namespace SmartChefAI.Services.Models;

public class AiMealIngredient
{
    public string Name { get; set; } = string.Empty;

    public decimal? Amount { get; set; }

    public string? Unit { get; set; }
}
