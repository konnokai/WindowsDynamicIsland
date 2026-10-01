using System.Security.Principal;
using System.Text.Json;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Receives local Claude Code hooks, including the Claude Desktop Code tab, without reading transcripts.</summary>
public sealed class ClaudeNotificationService : IDisposable
{
    private readonly HookPipeListener _listener;

    public ClaudeNotificationService(string? pipeName = null)
    {
        _listener = new HookPipeListener(
            pipeName ?? $"WindowsDynamicIsland.Claude.{WindowsIdentity.GetCurrent().User!.Value}",
            ParseNotification,
            notification => NotificationRaised?.Invoke(this, notification));
    }

    public event EventHandler<AgentNotification>? NotificationRaised;

    public void Start() => _listener.Start();

    public void Dispose() => _listener.Dispose();

    /// <summary>Maps documented hook signals only; a Stop means a response ended, not that work succeeded.</summary>
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
            var notificationType = GetString(root, "notification_type");
            var errorType = GetString(root, "error_type");
            // permission_prompt 和 PermissionRequest 可能同時觸發；文字相同，佇列會視為同一則而不重播。
            var (title, message, glyph, attention, working) = (eventName, notificationType) switch
            {
                ("UserPromptSubmit" or "PostToolUse", _) => ("Claude is working", "Processing your request.", "", false, true),
                ("PreToolUse", _) when tool == "AskUserQuestion" => ("Claude needs an answer", "Return to Claude to answer the question.", "", true, false),
                ("PermissionRequest", _) or ("Notification", "permission_prompt") => ("Claude needs permission", "Review the permission request in Claude.", "", true, false),
                ("Elicitation", _) or ("Notification", "elicitation_dialog" or "agent_needs_input") => ("Claude needs input", "Return to Claude to respond.", "", true, false),
                ("Notification", "idle_prompt") => ("Claude is waiting for you", "Return to Claude to continue.", "", false, false),
                ("Stop", _) => ("Claude response ready", "Return to Claude to review the response.", "", false, false),
                ("StopFailure", _) => ("Claude stopped with an error",
                    errorType.Length == 0 ? "Return to Claude to check the error." : $"Error: {errorType}.", "", true, false),
                ("SessionEnd", _) => ("Claude session ended", "The session has closed.", "", false, false),
                _ => (string.Empty, string.Empty, string.Empty, false, false)
            };
            return title.Length == 0 ? null : new AgentNotification(title, message, sessionId,
                glyph, attention, working, Source: "Claude");
        }
        catch (JsonException) { return null; }
    }

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
