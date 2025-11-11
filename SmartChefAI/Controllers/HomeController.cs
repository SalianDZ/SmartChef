using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartChefAI.Data;
using SmartChefAI.Models;
using SmartChefAI.Services;
using SmartChefAI.ViewModels.Home;

namespace SmartChefAI.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IUserService _userService;
    private readonly SmartChefContext _dbContext;

    public HomeController(
        ILogger<HomeController> logger,
        IUserService userService,
        SmartChefContext dbContext)
    {
        _logger = logger;
        _userService = userService;
        _dbContext = dbContext;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new HomeDashboardViewModel
        {
            IsAuthenticated = User.Identity?.IsAuthenticated == true
        };

        if (!model.IsAuthenticated)
        {
            return View(model);
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            model.IsAuthenticated = false;
            return View(model);
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            model.IsAuthenticated = false;
            return View(model);
        }

        model.GreetingName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        model.DailyCalorieTarget = user.DailyCalorieTarget;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayLog = await _dbContext.DailyNutritionLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == user.Id && l.Date == today, cancellationToken)
            .ConfigureAwait(false);

        if (todayLog != null)
        {
            model.CaloriesConsumedToday = todayLog.Calories;
            model.ProteinToday = todayLog.ProteinGrams;
            model.CarbsToday = todayLog.CarbohydrateGrams;
            model.FatToday = todayLog.FatGrams;
        }

        model.SavedMealsCount = await _dbContext.Meals
            .AsNoTracking()
            .CountAsync(m => m.UserId == user.Id, cancellationToken)
            .ConfigureAwait(false);

        var latestMeal = await _dbContext.Meals
            .AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new { m.Title, m.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (latestMeal != null)
        {
            model.LatestMealTitle = latestMeal.Title;
            model.LatestMealCreatedAtUtc = latestMeal.CreatedAtUtc;
        }

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
