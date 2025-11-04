namespace SmartChefAI.Services.Options;

public class NutritionApiOptions
{
    public bool UseRealApi { get; set; }

    public string? BaseUrl { get; set; }

    public string? Endpoint { get; set; } = "natural/nutrients";

    public string? AppIdHeader { get; set; } = "x-app-id";

    public string? AppId { get; set; }

    public string? ApiKeyHeader { get; set; } = "x-app-key";

    public string? ApiKey { get; set; }

    public string? BearerToken { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    public bool UseMockFallback { get; set; } = true;
}
