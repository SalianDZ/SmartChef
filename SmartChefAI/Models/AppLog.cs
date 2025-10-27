using System;

namespace SmartChefAI.Models;

public class AppLog
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string Level { get; set; } = "Information";

    public string Message { get; set; } = string.Empty;
}
