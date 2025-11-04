using System.Collections.Generic;

namespace SmartChefAI.Services.Models;

public class AiMealIdea
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Instructions { get; set; } = new();
}
