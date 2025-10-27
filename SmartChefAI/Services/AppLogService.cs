using SmartChefAI.Data;
using SmartChefAI.Models;

namespace SmartChefAI.Services;

public class AppLogService : IAppLogService
{
    private readonly SmartChefContext _dbContext;

    public AppLogService(SmartChefContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(string level, string message, CancellationToken cancellationToken = default)
    {
        var log = new AppLog
        {
            Level = level,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.AppLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
