using System;

namespace SmartChefAI.Models;

public class DailyNutritionLog
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Calories { get; set; }

    public decimal ProteinGrams { get; set; }

    public decimal CarbohydrateGrams { get; set; }

    public decimal FatGrams { get; set; }

    public User? User { get; set; }
}
