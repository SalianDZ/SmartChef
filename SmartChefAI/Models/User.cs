using System;
using System.Collections.Generic;

namespace SmartChefAI.Models;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int DailyCalorieTarget { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Meal> Meals { get; set; } = new List<Meal>();

    public ICollection<DailyNutritionLog> DailyNutritionLogs { get; set; } = new List<DailyNutritionLog>();
}
