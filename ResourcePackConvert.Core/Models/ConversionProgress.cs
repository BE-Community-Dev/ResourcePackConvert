namespace ResourcePackConvert.Core.Models;

/// <summary>
/// Reports conversion progress with a human-readable message and a percentage (0–100).
/// </summary>
public sealed class ConversionProgress
{
    /// <summary>Current progress message.</summary>
    public string Message { get; }

    /// <summary>Progress percentage, range 0–100.</summary>
    public int Percentage { get; }

    /// <summary>Optional stage name for external display (UI labels).</summary>
    public string? Stage { get; }

    public ConversionProgress(string message, int percentage, string? stage = null)
    {
        Message = message;
        Percentage = Math.Clamp(percentage, 0, 100);
        Stage = stage;
    }

    public override string ToString() => $"[{Percentage,3}%] {(Stage != null ? $"[{Stage}] " : "")}{Message}";
}
