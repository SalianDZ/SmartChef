using SmartChefAI.Models;

namespace SmartChefAI.Services;

public interface IUserService
{
    Task<(bool Success, string? Error, User? User)> RegisterAsync(string email, string password, string displayName, int dailyCalorieTarget, CancellationToken cancellationToken = default);

    Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
