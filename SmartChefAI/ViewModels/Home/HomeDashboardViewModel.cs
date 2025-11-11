using System;

namespace SmartChefAI.ViewModels.Home;

public class HomeDashboardViewModel
{
    private const decimal DefaultGoal = 2000m;

    public bool IsAuthenticated { get; set; }

    public string GreetingName { get; set; } = "there";

    public decimal DailyCalorieTarget { get; set; }

    public decimal CaloriesConsumedToday { get; set; }

    public decimal ProteinToday { get; set; }

    public decimal CarbsToday { get; set; }

    public decimal FatToday { get; set; }

    public int SavedMealsCount { get; set; }

    public string? LatestMealTitle { get; set; }

    public DateTime? LatestMealCreatedAtUtc { get; set; }

    public decimal EffectiveCalorieGoal => DailyCalorieTarget > 0 ? DailyCalorieTarget : DefaultGoal;

    public bool IsUsingDefaultGoal => DailyCalorieTarget <= 0;

    public decimal RemainingCalories =>
        EffectiveCalorieGoal > CaloriesConsumedToday
            ? Math.Round(EffectiveCalorieGoal - CaloriesConsumedToday, 2)
            : 0;

    public double CalorieProgressPercent
    {
        get
        {
            if (EffectiveCalorieGoal <= 0)
            {
                return 0;
            }

            var percent = (double)(CaloriesConsumedToday / EffectiveCalorieGoal) * 100d;
            return Math.Min(Math.Max(percent, 0d), 150d);
        }
    }

    public bool HasLoggedToday =>
        CaloriesConsumedToday > 0 || ProteinToday > 0 || CarbsToday > 0 || FatToday > 0;
}
