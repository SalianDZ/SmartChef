using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartChefAI.Data;
using SmartChefAI.Models;
using SmartChefAI.Services;
using SmartChefAI.Services.Models;
using SmartChefAI.Services.Options;
using SmartChefAI.ViewModels.Meals;

namespace SmartChefAI.Controllers;

[Authorize]
public class MealsController : Controller
{
    private readonly IMealGenerationService _dummyMealGenerationService;
    private readonly IMealGenerationService _chefMealGenerationService;
    private readonly IUserService _userService;
    private readonly SmartChefContext _dbContext;
    private readonly ILogger<MealsController> _logger;
    private readonly IAppLogService _appLogService;
    private readonly NutritionApiOptions _nutritionOptions;
    private readonly GeminiApiOptions _aiOptions;

    public MealsController(
        [FromKeyedServices("dummy")] IMealGenerationService dummyMealGenerationService,
        [FromKeyedServices("chef")] IMealGenerationService chefMealGenerationService,
        IUserService userService,
        SmartChefContext dbContext,
        ILogger<MealsController> logger,
        IAppLogService appLogService,
        IOptions<NutritionApiOptions> nutritionOptions,
        IOptions<GeminiApiOptions> aiOptions)
    {
        _dummyMealGenerationService = dummyMealGenerationService;
        _chefMealGenerationService = chefMealGenerationService;
        _userService = userService;
        _dbContext = dbContext;
        _logger = logger;
        _appLogService = appLogService;
        _nutritionOptions = nutritionOptions.Value;
        _aiOptions = aiOptions.Value;
    }

    [HttpGet("/dummy-ai")]
    [HttpGet("/meals/generate")]
    public Task<IActionResult> DummyAi(CancellationToken cancellationToken)
    {
        return GenerateGetInternalAsync("dummy", cancellationToken);
    }

    [HttpPost("/dummy-ai")]
    [HttpPost("/meals/generate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DummyAi(MealGenerationRequestViewModel request, string submitAction, int? removeIndex, CancellationToken cancellationToken)
    {
        return GeneratePostInternalAsync("dummy", request, submitAction, removeIndex, cancellationToken);
    }

    [HttpGet("/chef-ai")]
    public Task<IActionResult> ChefAi(CancellationToken cancellationToken)
    {
        return GenerateGetInternalAsync("chef", cancellationToken);
    }

    [HttpPost("/chef-ai")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ChefAi(MealGenerationRequestViewModel request, string submitAction, int? removeIndex, CancellationToken cancellationToken)
    {
        return GeneratePostInternalAsync("chef", request, submitAction, removeIndex, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string mealJson, string mode = "dummy", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mealJson))
        {
            TempData["Error"] = "Meal data was not provided.";
            return RedirectToMode(mode);
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
            return RedirectToMode(mode);
        }

        if (generatedMeal == null)
        {
            TempData["Error"] = "Generated meal was empty.";
            return RedirectToMode(mode);
        }

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            TempData["Error"] = "User session expired.";
            return RedirectToMode(mode);
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
            return RedirectToAction(nameof(DummyAi));
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

    private async Task<IActionResult> GenerateGetInternalAsync(string mode, CancellationToken cancellationToken)
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

        ConfigureModeMetadata(mode);
        return View("Generate", model);
    }

    private async Task<IActionResult> GeneratePostInternalAsync(
        string mode,
        MealGenerationRequestViewModel request,
        string submitAction,
        int? removeIndex,
        CancellationToken cancellationToken)
    {
        ConfigureModeMetadata(mode);

        request.Ingredients ??= new List<IngredientInputModel>();

        if (submitAction == "addIngredient")
        {
            request.Ingredients.Add(new IngredientInputModel());
            ModelState.Clear();
            return View("Generate", request);
        }

        if (submitAction == "removeIngredient" && removeIndex.HasValue)
        {
            if (removeIndex.Value >= 0 && removeIndex.Value < request.Ingredients.Count)
            {
                request.Ingredients.RemoveAt(removeIndex.Value);
            }

            ModelState.Clear();
            return View("Generate", request);
        }

        var hasAtLeastOneIngredient = NormalizeIngredients(request);

        await PopulateCalorieTargetIfRequestedAsync(request, cancellationToken).ConfigureAwait(false);

        if (!hasAtLeastOneIngredient)
        {
            ModelState.AddModelError(nameof(request.Ingredients), "Please provide at least one ingredient.");
        }

        if (!ModelState.IsValid)
        {
            return View("Generate", request);
        }

        var generationService = ResolveMealGenerationService(mode);

        var generatedMeal = await generationService.GenerateMealAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await _appLogService.LogAsync(
            "Information",
            $"{mode.ToUpperInvariant()} meal generated for user {User.Identity?.Name}: {generatedMeal.Title}",
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

    private IMealGenerationService ResolveMealGenerationService(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "chef" => _chefMealGenerationService,
            _ => _dummyMealGenerationService
        };
    }

    private IActionResult RedirectToMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "chef" => RedirectToAction(nameof(ChefAi)),
            _ => RedirectToAction(nameof(DummyAi))
        };
    }

    private void ConfigureModeMetadata(string mode)
    {
        var normalized = mode?.ToLowerInvariant() == "chef" ? "chef" : "dummy";
        ViewData["Mode"] = normalized;
        ViewData["ModeTitle"] = normalized == "chef" ? "ChefAI Gourmet" : "Dummy AI Kitchen";
        ViewData["ModeSubtitle"] = normalized == "chef"
            ? "Leverages live nutrition data and generative AI to compose refined meals."
            : "Uses mock AI logic to craft approachable recipes with simulated macros.";
        ViewData["ModePrimaryAction"] = normalized == "chef" ? "ChefAi" : "DummyAi";

        if (normalized != "chef")
        {
            ViewData["ModeWarnings"] = null;
            return;
        }

        var warnings = new List<string>();

        if (!_nutritionOptions.UseRealApi)
        {
            warnings.Add("Real nutrition API is not configured. ChefAI will fall back to simulated macros until credentials are provided.");
        }

        if (!_aiOptions.Enabled)
        {
            warnings.Add("Gemini AI integration is disabled. ChefAI will use template-based descriptions.");
        }
        else if (!IsAiKeyAvailable())
        {
            var envName = string.IsNullOrWhiteSpace(_aiOptions.ApiKeyEnvironmentVariable)
                ? "GeminiApi.ApiKey"
                : _aiOptions.ApiKeyEnvironmentVariable!;
            warnings.Add($"Gemini API key is missing. Provide a key via configuration or environment variable '{envName}'.");
        }

        ViewData["ModeWarnings"] = warnings.Any() ? warnings : null;
    }

    private bool IsAiKeyAvailable()
    {
        if (!string.IsNullOrWhiteSpace(_aiOptions.ApiKey))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_aiOptions.ApiKeyEnvironmentVariable))
        {
            var fromEnv = Environment.GetEnvironmentVariable(_aiOptions.ApiKeyEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(fromEnv);
        }

        return false;
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
