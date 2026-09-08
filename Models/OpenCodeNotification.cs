namespace WindowsDynamicIsland.Models;

public sealed record OpenCodeNotification(
    string Title,
    string Message,
    string SessionId,
    string Glyph,
    bool RequiresAttention,
    bool IsVisualizerActive = false,
    string? RequestId = null,
    string? Question = null,
    IReadOnlyList<string>? Options = null);
