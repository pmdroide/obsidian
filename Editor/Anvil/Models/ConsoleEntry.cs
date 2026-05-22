namespace Anvil.Models;

public enum ConsoleLevel
{
    Info,
    Warning,
    Error,
}

public class ConsoleEntry
{
    public ConsoleLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public string? Source { get; init; }
}
