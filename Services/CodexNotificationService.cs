using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Receives local Codex hooks without reading transcripts or changing approval decisions.</summary>
public sealed class CodexNotificationService : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _runTask;

    public CodexNotificationService(string? pipeName = null)
    {
        _pipeName = pipeName ?? $"WindowsDynamicIsland.Codex.{WindowsIdentity.GetCurrent().User!.Value}";
    }

    public event EventHandler<AgentNotification>? NotificationRaised;

    public void Start() => _runTask ??= RunAsync(_stopSource.Token);

    public void Dispose()
    {
        _stopSource.Cancel();
    }

    /// <summary>Keeps accepting hooks while other clients finish writing; cancellation closes pending reads.</summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var clients = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }

                clients.RemoveAll(task => task.IsCompleted);
                clients.Add(ReadClientAsync(pipe, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { /* Another island instance may own the pipe. */ }
        catch (UnauthorizedAccessException) { /* Notification failure must not prevent startup. */ }
        finally
        {
            await Task.WhenAll(clients).ConfigureAwait(false);
        }
    }

    private async Task ReadClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8))
        {
            try
            {
                var payload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (ParseNotification(payload) is { } notification && !cancellationToken.IsCancellationRequested)
                    NotificationRaised?.Invoke(this, notification);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { /* A hook may be interrupted before its write completes. */ }
        }
    }

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
                "UserPromptSubmit" => ("Codex is working", "Processing your request.", "\uE768", false, true),
                "PreToolUse" when isQuestion => ("Codex needs an answer", "Return to Codex to answer the question.", "\uE946", true, false),
                "PreToolUse" or "PostToolUse" => ("Codex is working", "Processing your request.", "\uE768", false, true),
                "PermissionRequest" => ("Codex needs permission", "Review the permission request in Codex.", "\uE7BA", true, false),
                "Stop" => ("Codex response ready", "Return to Codex to review the response.", "\uE73E", false, false),
                "Interrupt" => ("Codex interrupted", "The current turn was interrupted.", "\uE711", false, false),
                "SessionEnd" => ("Codex session ended", "The session has closed.", "\uE73E", false, false),
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
