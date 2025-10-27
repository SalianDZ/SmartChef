using System.ComponentModel.DataAnnotations;

namespace SmartChefAI.ViewModels.Meals;

public class IngredientInputModel
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal? Quantity { get; set; }

    [StringLength(50)]
    public string? Unit { get; set; }
}
