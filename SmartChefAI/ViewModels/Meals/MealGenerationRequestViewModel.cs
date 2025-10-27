using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SmartChefAI.ViewModels.Meals;

public class MealGenerationRequestViewModel
{
    [Required]
    [MinLength(1, ErrorMessage = "Add at least one ingredient.")]
    public List<IngredientInputModel> Ingredients { get; set; } = new();

    [Range(0, 20000)]
    [Display(Name = "Calorie Target")]
    public int CalorieTarget { get; set; }

    public bool UseUserCalorieTarget { get; set; }
}
