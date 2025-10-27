using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartChefAI.Data;
using SmartChefAI.Models;

namespace SmartChefAI.Services;

public class UserService : IUserService
{
    private readonly SmartChefContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserService(SmartChefContext dbContext, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<(bool Success, string? Error, User? User)> RegisterAsync(string email, string password, string displayName, int dailyCalorieTarget, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser != null)
        {
            return (false, "Email is already registered.", null);
        }

        var user = new User
        {
            Email = normalizedEmail,
            DisplayName = displayName.Trim(),
            DailyCalorieTarget = dailyCalorieTarget,
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            return (false, $"Failed to create user: {ex.Message}", null);
        }

        return (true, null, user);
    }

    public async Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null)
        {
            return null;
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return verificationResult == PasswordVerificationResult.Success ? user : null;
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
    }
}
