namespace SmartChefAI.Services.Options;

public class GeminiApiOptions
{
    public bool Enabled { get; set; }

    public string Model { get; set; } = "gemini-2.5-pro";

    public string ProjectId { get; set; } = string.Empty;

    public string Location { get; set; } = "us-central1";

    public string DefaultSystemPrompt { get; set; } =
        "You are ChefAI, an assistant that crafts healthy, flavorful meals. Respond with concise JSON.";

    public int TimeoutSeconds { get; set; } = 15;
}
