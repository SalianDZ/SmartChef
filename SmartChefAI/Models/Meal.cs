using System;
using System.Collections.Generic;

namespace SmartChefAI.Models;

public class Meal
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string InputSummary { get; set; } = string.Empty;

    public decimal TotalCalories { get; set; }

    public decimal ProteinGrams { get; set; }

    public decimal CarbohydrateGrams { get; set; }

    public decimal FatGrams { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<MealIngredient> Ingredients { get; set; } = new List<MealIngredient>();

    public ICollection<MealInstruction> Instructions { get; set; } = new List<MealInstruction>();
}
