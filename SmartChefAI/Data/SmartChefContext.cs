using Microsoft.EntityFrameworkCore;
using SmartChefAI.Models;

namespace SmartChefAI.Data;

public class SmartChefContext : DbContext
{
    public SmartChefContext(DbContextOptions<SmartChefContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealIngredient> MealIngredients => Set<MealIngredient>();
    public DbSet<MealInstruction> MealInstructions => Set<MealInstruction>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.DailyCalorieTarget)
                .HasDefaultValue(0);

            entity.Property(e => e.CreatedAtUtc)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasMany(e => e.Meals)
                .WithOne(m => m.User)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Meal>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Description)
                .HasMaxLength(1000);

            entity.Property(e => e.InputSummary)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.CreatedAtUtc)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.TotalCalories)
                .HasPrecision(10, 2);

            entity.Property(e => e.ProteinGrams)
                .HasPrecision(10, 2);

            entity.Property(e => e.CarbohydrateGrams)
                .HasPrecision(10, 2);

            entity.Property(e => e.FatGrams)
                .HasPrecision(10, 2);
        });

        modelBuilder.Entity<MealIngredient>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Unit)
                .HasMaxLength(50);

            entity.Property(e => e.Amount)
                .HasPrecision(10, 2);

            entity.Property(e => e.Calories)
                .HasPrecision(10, 2);

            entity.HasOne(e => e.Meal)
                .WithMany(m => m.Ingredients)
                .HasForeignKey(e => e.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MealInstruction>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Text)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.StepNumber)
                .IsRequired();

            entity.HasOne(e => e.Meal)
                .WithMany(m => m.Instructions)
                .HasForeignKey(e => e.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Level)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.CreatedAtUtc)
                .HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
