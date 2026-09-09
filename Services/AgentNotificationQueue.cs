using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Keeps attention requests visible across providers; only the same session replaces its status.</summary>
public sealed class AgentNotificationQueue
{
    private readonly List<AgentNotification> _pending = [];
    private readonly Dictionary<(string Source, string Session), AgentNotification> _latest = [];

    public AgentNotification? Current => _pending.FirstOrDefault(item => item.RequiresAttention && item.RequestId is not null)
        ?? _pending.FirstOrDefault(item => item.RequiresAttention)
        ?? _pending.FirstOrDefault();

    public void Update(AgentNotification notification)
    {
        var key = (notification.Source, notification.SessionId);
        if (_latest.TryGetValue(key, out var previous) && previous == notification) return;
        _latest[key] = notification;
        // Explicit questions live until resolved or dismissed. Background status hooks
        // from the same session must not erase an unanswered request.
        var index = _pending.FindIndex(item => item.Source == notification.Source && item.SessionId == notification.SessionId
            && (item.RequestId == notification.RequestId || !(item.RequiresAttention && item.RequestId is not null)));
        if (index >= 0) _pending[index] = notification;
        else _pending.Add(notification);
    }

    public void Dismiss(AgentNotification notification) => _pending.Remove(notification);

    public void ResolveQuestion(string source, string requestId) =>
        _pending.RemoveAll(item => item.Source == source && item.RequestId == requestId);
}
