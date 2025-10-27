namespace SmartChefAI.Models;

public class MealInstruction
{
    public int Id { get; set; }

    public int MealId { get; set; }

    public Meal? Meal { get; set; }

    public int StepNumber { get; set; }

    public string Text { get; set; } = string.Empty;
}
