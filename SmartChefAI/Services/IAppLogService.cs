namespace SmartChefAI.Services;

public interface IAppLogService
{
    Task LogAsync(string level, string message, CancellationToken cancellationToken = default);
}
