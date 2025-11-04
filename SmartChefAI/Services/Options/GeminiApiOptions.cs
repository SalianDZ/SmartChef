namespace SmartChefAI.Services.Options;

public class GeminiApiOptions
{
    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    public string? ApiKeyEnvironmentVariable { get; set; } = "GEMINI_API_KEY";

    public string Model { get; set; } = "gemini-1.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";

    public string DefaultSystemPrompt { get; set; } =
        "You are ChefAI, an assistant that crafts healthy, flavorful meals. Respond with concise JSON.";

    public int TimeoutSeconds { get; set; } = 15;
}
