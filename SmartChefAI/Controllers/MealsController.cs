using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartChefAI.Data;
using SmartChefAI.Models;
using SmartChefAI.Services;
using SmartChefAI.Services.Models;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Controllers;

[Authorize]
public class MealsController : Controller
{
    private readonly IMealGenerationService _mealGenerationService;
    private readonly IUserService _userService;
    private readonly SmartChefContext _dbContext;
    private readonly ILogger<MealsController> _logger;
    private readonly IAppLogService _appLogService;

    public MealsController(
        IMealGenerationService mealGenerationService,
        IUserService userService,
        SmartChefContext dbContext,
        ILogger<MealsController> logger,
        IAppLogService appLogService)
    {
        _mealGenerationService = mealGenerationService;
        _userService = userService;
        _dbContext = dbContext;
        _logger = logger;
        _appLogService = appLogService;
    }

    [HttpGet]
    public async Task<IActionResult> Generate(CancellationToken cancellationToken)
    {
        var model = new MealGenerationRequestViewModel
        {
            Ingredients = new List<IngredientInputModel> { new() }
        };

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (user != null && user.DailyCalorieTarget > 0)
        {
            model.CalorieTarget = user.DailyCalorieTarget;
            model.UseUserCalorieTarget = true;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(MealGenerationRequestViewModel request, string submitAction, int? removeIndex, CancellationToken cancellationToken)
    {
        request.Ingredients ??= new List<IngredientInputModel>();

        if (submitAction == "addIngredient")
        {
            request.Ingredients.Add(new IngredientInputModel());
            ModelState.Clear();
            return View(request);
        }

        if (submitAction == "removeIngredient" && removeIndex.HasValue)
        {
            if (removeIndex.Value >= 0 && removeIndex.Value < request.Ingredients.Count)
            {
                request.Ingredients.RemoveAt(removeIndex.Value);
            }

            ModelState.Clear();
            return View(request);
        }

        var hasAtLeastOneIngredient = NormalizeIngredients(request);

        await PopulateCalorieTargetIfRequestedAsync(request, cancellationToken).ConfigureAwait(false);

        if (!hasAtLeastOneIngredient)
        {
            ModelState.AddModelError(nameof(request.Ingredients), "Please provide at least one ingredient.");
        }

        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var generatedMeal = await _mealGenerationService.GenerateMealAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await _appLogService.LogAsync(
            "Information",
            $"Meal generated for user {User.Identity?.Name}: {generatedMeal.Title}",
            cancellationToken);

        var resultViewModel = new MealGenerationResultViewModel
        {
            Request = request,
            Meal = generatedMeal,
            MealJson = JsonSerializer.Serialize(generatedMeal, new JsonSerializerOptions
            {
                WriteIndented = false
            })
        };

        return View("Result", resultViewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string mealJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mealJson))
        {
            TempData["Error"] = "Meal data was not provided.";
            return RedirectToAction(nameof(Generate));
        }

        GeneratedMeal? generatedMeal;
        try
        {
            generatedMeal = JsonSerializer.Deserialize<GeneratedMeal>(mealJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize meal JSON.");
            await _appLogService.LogAsync("Error", $"Meal save failed due to invalid JSON: {ex.Message}");
            TempData["Error"] = "Could not interpret the generated meal.";
            return RedirectToAction(nameof(Generate));
        }

        if (generatedMeal == null)
        {
            TempData["Error"] = "Generated meal was empty.";
            return RedirectToAction(nameof(Generate));
        }

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            TempData["Error"] = "User session expired.";
            return RedirectToAction(nameof(Generate));
        }

        var meal = new Meal
        {
            UserId = user.Id,
            Title = generatedMeal.Title,
            Description = generatedMeal.Description,
            InputSummary = generatedMeal.InputSummary,
            TotalCalories = generatedMeal.Nutrition.TotalCalories,
            ProteinGrams = generatedMeal.Nutrition.TotalProteinGrams,
            CarbohydrateGrams = generatedMeal.Nutrition.TotalCarbohydrateGrams,
            FatGrams = generatedMeal.Nutrition.TotalFatGrams,
            CreatedAtUtc = DateTime.UtcNow,
            Ingredients = generatedMeal.Ingredients.Select(i => new MealIngredient
            {
                Name = i.Name,
                Amount = i.Amount,
                Unit = i.Unit,
                Calories = i.Calories
            }).ToList(),
            Instructions = generatedMeal.Instructions.Select(i => new MealInstruction
            {
                StepNumber = i.StepNumber,
                Text = i.Text
            }).ToList()
        };

        _dbContext.Meals.Add(meal);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _appLogService.LogAsync("Information", $"Meal saved: {meal.Title} for user {user.Email}", cancellationToken);
        TempData["Success"] = "Meal saved successfully.";
        return RedirectToAction(nameof(Saved));
    }

    [HttpGet]
    public async Task<IActionResult> Saved(CancellationToken cancellationToken, int page = 1, int pageSize = 10)
    {
        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return RedirectToAction(nameof(Generate));
        }

        var query = _dbContext.Meals
            .AsNoTracking()
            .Include(m => m.Ingredients)
            .Include(m => m.Instructions)
            .Where(m => m.UserId == user.Id)
            .OrderByDescending(m => m.CreatedAtUtc);

        var meals = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return View(meals);
    }

    private async Task PopulateCalorieTargetIfRequestedAsync(MealGenerationRequestViewModel request, CancellationToken cancellationToken)
    {
        if (!request.UseUserCalorieTarget)
        {
            return;
        }

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (user?.DailyCalorieTarget > 0)
        {
            request.CalorieTarget = user.DailyCalorieTarget;
        }
    }

    private bool NormalizeIngredients(MealGenerationRequestViewModel request)
    {
        var hasAtLeastOne = request.Ingredients.Any(i => !string.IsNullOrWhiteSpace(i.Name));

        request.Ingredients = request.Ingredients
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new IngredientInputModel
            {
                Name = i.Name.Trim(),
                Quantity = i.Quantity,
                Unit = string.IsNullOrWhiteSpace(i.Unit) ? null : i.Unit.Trim()
            })
            .ToList();

        return hasAtLeastOne;
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await _userService.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}
