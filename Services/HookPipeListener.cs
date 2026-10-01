using System.IO.Pipes;
using System.Text;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

/// <summary>Accepts one JSON payload per local hook connection and forwards parsed notifications.</summary>
internal sealed class HookPipeListener : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<string, AgentNotification?> _parser;
    private readonly Action<AgentNotification> _raise;
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _runTask;

    public HookPipeListener(string pipeName, Func<string, AgentNotification?> parser, Action<AgentNotification> raise)
    {
        _pipeName = pipeName;
        _parser = parser;
        _raise = raise;
    }

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
                if (_parser(payload) is { } notification && !cancellationToken.IsCancellationRequested)
                    _raise(notification);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { /* A hook may be interrupted before its write completes. */ }
        }
    }
}
