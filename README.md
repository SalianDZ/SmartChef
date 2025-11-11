# SmartChef
# SmartChefAI

SmartChefAI is an ASP.NET Core 8 MVC application that mixes nutrition tracking with AI‑generated meal ideas.  
Users enter one or more ingredients, optionally set a calorie target, and SmartChef responds with a complete meal:
title, description, instructions, macros, and optional suggestions. Two modes are available:

- **Dummy AI** – instant mock meals powered by a simulated nutrition service (great for demos/offline).
- **ChefAI** – real integration with Google’s Gemini (Vertex AI) plus actual nutrition calculations.

Key features:

- Cookie-based authentication with custom `User` entity, registration, login, and saved calorie goal.
- Meal generation pipeline with services for nutrition lookup, AI idea extraction, and result composition.
- Daily nutrition log that updates when a meal is saved “and applied”, fueling the dashboard calorie tracker.
- Modern UI: dual AI pages, animated dashboard with circular progress, revamped auth pages, and an About screen.
- Logging infrastructure (`AppLogService`) and Gemini request tracing for easier debugging.

## Tech stack

- **Backend**: ASP.NET Core 8 MVC, Entity Framework Core, SQL Server (local or Docker), ASP.NET Identity password hashing.
- **AI**: Google Cloud Vertex AI (Gemini) via `GenerateContent` plus a fallback dummy service.
- **Frontend**: Bootstrap 5, custom CSS animations, Razor views.
- **Dev tooling**: .NET CLI, EF Core migrations, Docker SQL Server container.

## Getting started

1. **Requirements**
   - .NET 8 SDK
   - SQL Server (local instance or Docker container)
   - Google Cloud account with Vertex AI enabled (optional—needed for ChefAI mode)

2. **Clone & restore**
   ```bash
   git clone <repo-url>
   cd SmartChefAI
   dotnet restore
   ```

3. **Configure database**
   - Update `appsettings.Development.json` with your SQL Server connection string (`ConnectionStrings:DefaultConnection`).
   - Apply migrations:
     ```bash
     dotnet ef database update
     ```

4. **Configure Gemini (optional but recommended)**
   - Set `GeminiApi:Enabled` to `true`, and supply `ProjectId`, `Location`, and a valid model (e.g., `gemini-2.5-pro`).
   - Ensure `GOOGLE_APPLICATION_CREDENTIALS` points to a service-account JSON or run `gcloud auth application-default login`.

5. **Run**
   ```bash
   dotnet run
   ```
   Visit `https://localhost:5001` (or the port shown in the console). Register a user, add ingredients, and test both AI modes.

## Project structure (high level)

```
Controllers/
  HomeController.cs           -> Dashboard, Privacy, About
  MealsController.cs          -> Dummy/Chef AI flows, saving, logs
Data/
  SmartChefContext.cs         -> EF Core DbContext
Models/
  User, Meal, MealIngredient, DailyNutritionLog, AppLog, etc.
Services/
  ChefMealGenerationService   -> Real nutrition + Gemini
  MockMealGenerationService   -> Dummy AI fallback
  GeminiAiTextService         -> Vertex AI client
  NutritionService            -> Dummy JSON nutrition provider
ViewModels/
  MealGenerationRequest/Result, Account view models
Views/
  Meals, Account, Home, Shared layout
```

## Future improvements

- AI vision input (photo -> ingredients) and automatic meal creation.
- Community features: sharing, rating, and cloning saved meals.
- Deeper analytics dashboard with weekly trends and protein/carb/fat targets.
- Integrations with wearables or smart scales for automatic calorie adjustments.

---

Feel free to open issues or suggest enhancements. Happy cooking with SmartChefAI!
