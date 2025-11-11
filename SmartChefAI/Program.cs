using Google.Cloud.AIPlatform.V1;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartChefAI.Data;
using SmartChefAI.Models;
using SmartChefAI.Services;
using SmartChefAI.Services.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SmartChefContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

    options.UseSqlServer(connectionString);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAppLogService, AppLogService>();
builder.Services.Configure<GeminiApiOptions>(builder.Configuration.GetSection("GeminiApi"));

builder.Services.AddSingleton(sp =>
{
    var geminiOptions = sp.GetRequiredService<IOptions<GeminiApiOptions>>().Value;
    var location = string.IsNullOrWhiteSpace(geminiOptions.Location) ? "us-central1" : geminiOptions.Location;
    var endpoint = $"{location}-aiplatform.googleapis.com";

    var predictionBuilder = new PredictionServiceClientBuilder
    {
        Endpoint = endpoint
    };

    return predictionBuilder.Build();
});

builder.Services.AddHttpClient<INutritionService, NutritionService>(client =>
{
    client.BaseAddress = new Uri("https://dummyjson.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddKeyedScoped<IMealGenerationService, MockMealGenerationService>("dummy");
builder.Services.AddKeyedScoped<IMealGenerationService, ChefMealGenerationService>("chef");
builder.Services.AddScoped<IAiTextService, GeminiAiTextService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
