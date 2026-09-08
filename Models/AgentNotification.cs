namespace WindowsDynamicIsland.Models;

public sealed record AgentNotification(
    string Title,
    string Message,
    string SessionId,
    string Glyph,
    bool RequiresAttention,
    bool IsVisualizerActive = false,
    string? RequestId = null,
    string? Question = null,
    IReadOnlyList<string>? Options = null,
    string Source = "OpenCode");
