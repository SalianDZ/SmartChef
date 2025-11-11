using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartChefAI.Data;

namespace SmartChefAI.Controllers;

[Authorize]
public class LogsController : Controller
{
    private readonly SmartChefContext _dbContext;

    public LogsController(SmartChefContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IActionResult> Index(int take = 100)
    {
        var logs = await _dbContext.AppLogs
            .AsNoTracking()
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(take, 10, 500))
            .ToListAsync();

        return View(logs);
    }
}
