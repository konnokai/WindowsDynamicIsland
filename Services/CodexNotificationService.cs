using System.Security.Principal;
using System.Text.Json;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Receives local Codex hooks without reading transcripts or changing approval decisions.</summary>
public sealed class CodexNotificationService : IDisposable
{
    private readonly HookPipeListener _listener;

    public CodexNotificationService(string? pipeName = null)
    {
        _listener = new HookPipeListener(
            pipeName ?? $"WindowsDynamicIsland.Codex.{WindowsIdentity.GetCurrent().User!.Value}",
            ParseNotification,
            notification => NotificationRaised?.Invoke(this, notification));
    }

    public event EventHandler<AgentNotification>? NotificationRaised;

    public void Start() => _listener.Start();

    public void Dispose() => _listener.Dispose();

    /// <summary>Maps only documented hook signals; a Stop means a response ended, not that work succeeded.</summary>
    internal static AgentNotification? ParseNotification(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var sessionId = GetString(root, "session_id");
            if (string.IsNullOrWhiteSpace(sessionId)) return null;
            var eventName = GetString(root, "hook_event_name");
            var tool = GetString(root, "tool_name");
            var isQuestion = tool is "request_user_input" or "request_user_input_async";
            var (title, message, glyph, attention, working) = eventName switch
            {
                "UserPromptSubmit" => ("Codex is working", "Processing your request.", "", false, true),
                "PreToolUse" when isQuestion => ("Codex needs an answer", "Return to Codex to answer the question.", "", true, false),
                "PreToolUse" or "PostToolUse" => ("Codex is working", "Processing your request.", "", false, true),
                "PermissionRequest" => ("Codex needs permission", "Review the permission request in Codex.", "", true, false),
                "Stop" => ("Codex response ready", "Return to Codex to review the response.", "", false, false),
                "Interrupt" => ("Codex interrupted", "The current turn was interrupted.", "", false, false),
                "SessionEnd" => ("Codex session ended", "The session has closed.", "", false, false),
                _ => (string.Empty, string.Empty, string.Empty, false, false)
            };
            return title.Length == 0 ? null : new AgentNotification(title, message, sessionId,
                glyph, attention, working, Source: "Codex");
        }
        catch (JsonException) { return null; }
    }

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
