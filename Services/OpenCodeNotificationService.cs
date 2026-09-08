using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Reads OpenCode/OpenChamber's local SSE stream and emits user-facing activity.</summary>
public sealed class OpenCodeNotificationService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, string> _sessionStatuses = [];
    private CancellationTokenSource? _stopSource;
    private Task? _runTask;

    public event EventHandler<AgentNotification>? NotificationRaised;
    public event EventHandler<string>? QuestionResolved;

    public async Task ReplyToQuestionAsync(AgentNotification notification, string answer)
    {
        if (string.IsNullOrWhiteSpace(notification.RequestId) || string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        using var content = JsonContent.Create(new { answers = new[] { new[] { answer } } });
        using var response = await _httpClient.PostAsync(
            GetQuestionReplyEndpoint(notification.RequestId),
            content);
        response.EnsureSuccessStatusCode();
    }

    public void Start()
    {
        if (_runTask is not null)
        {
            return;
        }

        _stopSource = new CancellationTokenSource();
        _runTask = RunAsync(_stopSource.Token);
    }

    public void Dispose()
    {
        _stopSource?.Cancel();
        _httpClient.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var endpoint in GetEndpoints())
            {
                try
                {
                    await ReadEndpointAsync(endpoint, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    // A connection timeout is expected when the local server is not running.
                }
                catch (HttpRequestException)
                {
                    // OpenChamber/OpenCode may start after the island; keep retrying locally.
                }
                catch (IOException)
                {
                    // The SSE connection can close during a server restart.
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadEndpointAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await _httpClient.GetAsync(
            endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            connectTimeout.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                data.AppendLine(line[5..].TrimStart());
                continue;
            }

            if (line.Length == 0 && data.Length > 0)
            {
                ProcessPayload(data.ToString());
                data.Clear();
            }
        }
    }

    private void ProcessPayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("payload", out var wrappedPayload) && wrappedPayload.ValueKind == JsonValueKind.Object)
            {
                root = wrappedPayload;
            }

            var type = root.TryGetProperty("type", out var typeProperty)
                ? typeProperty.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            var properties = root.TryGetProperty("properties", out var propertiesProperty)
                ? propertiesProperty
                : default;

            switch (type)
            {
                case "session.status":
                    ProcessSessionStatus(properties);
                    break;
                case "openchamber:session-status":
                    ProcessOpenChamberStatus(properties);
                    break;
                case "session.error":
                    RaiseAttentionNotification(properties, "OpenCode error", "The session reported an error.", "\uE783");
                    break;
                case "permission.asked":
                    RaiseAttentionNotification(properties, "Permission needed", "OpenCode is waiting for permission.", "\uE7BA");
                    break;
                case "question.asked":
                    ProcessQuestion(properties);
                    break;
                case "question.replied":
                case "question.rejected":
                    var requestId = GetString(properties, "requestID");
                    if (!string.IsNullOrWhiteSpace(requestId))
                    {
                        QuestionResolved?.Invoke(this, requestId);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore heartbeat or malformed payloads and keep the stream alive.
        }
    }

    private void ProcessSessionStatus(JsonElement properties)
    {
        var sessionId = GetString(properties, "sessionID");
        var status = properties.TryGetProperty("status", out var statusProperty)
            ? GetString(statusProperty, "type")
            : string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        _sessionStatuses.TryGetValue(sessionId, out var previousStatus);
        _sessionStatuses[sessionId] = status;
        if (status is "busy" or "retry")
        {
            RaiseNotification(
                status == "busy" ? "OpenCode is working" : "OpenCode is retrying",
                status == "busy" ? "A session is processing your request." : "A session is retrying the request.",
                sessionId,
                status == "busy" ? "\uE768" : "\uE72C",
                false,
                isVisualizerActive: status == "busy");
        }
        else if (status == "idle" && (previousStatus is "busy" or "retry"))
        {
            RaiseNotification("OpenCode completed", "The session is ready for review.", sessionId, "\uE73E", false);
        }
    }

    private void ProcessOpenChamberStatus(JsonElement properties)
    {
        var sessionId = GetString(properties, "sessionID");
        var status = GetString(properties, "status");
        var needsAttention = properties.TryGetProperty("needsAttention", out var attention) && attention.GetBoolean();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        _sessionStatuses.TryGetValue(sessionId, out var previousStatus);
        _sessionStatuses[sessionId] = status;
        if (needsAttention || (status == "idle" && (previousStatus is "busy" or "retry")))
        {
            RaiseNotification(
                needsAttention ? "OpenCode needs attention" : "OpenCode completed",
                needsAttention ? "OpenChamber is waiting for your input." : "The session is ready for review.",
                sessionId,
                needsAttention ? "\uE7BA" : "\uE73E",
                needsAttention,
                isVisualizerActive: false);
        }
        else if (status is "busy" or "retry")
        {
            RaiseNotification(
                status == "busy" ? "OpenCode is working" : "OpenCode is retrying",
                status == "busy" ? "A session is processing your request." : "A session is retrying the request.",
                sessionId,
                status == "busy" ? "\uE768" : "\uE72C",
                false,
                isVisualizerActive: status == "busy");
        }
    }

    private void RaiseAttentionNotification(JsonElement properties, string title, string message, string glyph)
    {
        var sessionId = GetString(properties, "sessionID");
        RaiseNotification(title, message, sessionId, glyph, true);
    }

    private void ProcessQuestion(JsonElement properties)
    {
        var requestId = GetString(properties, "requestID");
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = GetString(properties, "id");
        }
        var sessionId = GetString(properties, "sessionID");
        if (string.IsNullOrWhiteSpace(requestId) ||
            !properties.TryGetProperty("questions", out var questions) ||
            questions.ValueKind != JsonValueKind.Array ||
            questions.GetArrayLength() == 0)
        {
            return;
        }

        var question = questions[0];
        var header = GetString(question, "header");
        var text = GetString(question, "question");
        var options = new List<string>();
        if (question.TryGetProperty("options", out var optionItems) && optionItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in optionItems.EnumerateArray())
            {
                var label = GetString(option, "label");
                if (!string.IsNullOrWhiteSpace(label))
                {
                    options.Add(label);
                }
            }
        }

        RaiseNotification(
            string.IsNullOrWhiteSpace(header) ? "OpenCode needs an answer" : header,
            string.IsNullOrWhiteSpace(text) ? "A session is waiting for your response." : text,
            sessionId,
            "\uE946",
            true,
            requestId: requestId,
            question: text,
            options: options);
    }

    private void RaiseNotification(
        string title,
        string message,
        string sessionId,
        string glyph,
        bool requiresAttention,
        bool isVisualizerActive = false,
        string? requestId = null,
        string? question = null,
        IReadOnlyList<string>? options = null) =>
        NotificationRaised?.Invoke(this, new AgentNotification(title, message, sessionId, glyph, requiresAttention, isVisualizerActive, requestId, question, options));

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static IEnumerable<Uri> GetEndpoints()
    {
        var openChamberHost = Environment.GetEnvironmentVariable("OPENCHAMBER_HOST");
        if (!string.IsNullOrWhiteSpace(openChamberHost) && Uri.TryCreate(openChamberHost, UriKind.Absolute, out var openChamberUri))
        {
            yield return new Uri(openChamberUri, "/api/global/event");
            yield break;
        }

        var openCodeHost = Environment.GetEnvironmentVariable("OPENCODE_HOST");
        if (!string.IsNullOrWhiteSpace(openCodeHost) && Uri.TryCreate(openCodeHost, UriKind.Absolute, out var openCodeUri))
        {
            yield return new Uri(openCodeUri, "/global/event");
            yield break;
        }

        yield return new Uri("http://127.0.0.1:57123/api/global/event");
        yield return new Uri("http://127.0.0.1:4096/global/event");
    }

    private static Uri GetQuestionReplyEndpoint(string requestId)
    {
        var escapedRequestId = Uri.EscapeDataString(requestId);
        var openChamberHost = Environment.GetEnvironmentVariable("OPENCHAMBER_HOST");
        if (!string.IsNullOrWhiteSpace(openChamberHost) && Uri.TryCreate(openChamberHost, UriKind.Absolute, out var openChamberUri))
        {
            return new Uri(openChamberUri, $"/api/question/{escapedRequestId}/reply");
        }

        var openCodeHost = Environment.GetEnvironmentVariable("OPENCODE_HOST");
        if (!string.IsNullOrWhiteSpace(openCodeHost) && Uri.TryCreate(openCodeHost, UriKind.Absolute, out var openCodeUri))
        {
            return new Uri(openCodeUri, $"/question/{escapedRequestId}/reply");
        }

        return new Uri($"http://127.0.0.1:57123/api/question/{escapedRequestId}/reply");
    }
}
