namespace SmartChefAI.Models;

public class MealIngredient
{
    public int Id { get; set; }

    public int MealId { get; set; }

    public Meal? Meal { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal? Amount { get; set; }

    public string? Unit { get; set; }

    public decimal? Calories { get; set; }
}
