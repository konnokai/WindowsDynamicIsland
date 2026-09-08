# Codex supplies one JSON object on stdin. Forward only event metadata, never
# prompts, tool arguments, responses, or transcript paths. Hooks are observers.
$ErrorActionPreference = 'Stop'
$taskPipe = $null
$taskWriter = $null
try {
    $taskEvent = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $taskPayload = @{
        hook_event_name = [string]$taskEvent.hook_event_name
        session_id = [string]$taskEvent.session_id
        tool_name = [string]$taskEvent.tool_name
    } | ConvertTo-Json -Compress
    $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskPipe = [IO.Pipes.NamedPipeClientStream]::new('.', "WindowsDynamicIsland.Codex.$taskSid", [IO.Pipes.PipeDirection]::Out)
    # Zero means no waiting: notifications must not hold up Codex when the island is closed.
    $taskPipe.Connect(0)
    $taskWriter = [IO.StreamWriter]::new($taskPipe, [Text.UTF8Encoding]::new($false))
    $taskWriter.Write($taskPayload)
    $taskWriter.Flush()
} catch {
    # Missing island, interrupted writes, and invalid input have no effect on Codex.
} finally {
    if ($null -ne $taskWriter) { $taskWriter.Dispose() }
    if ($null -ne $taskPipe) { $taskPipe.Dispose() }
}
# Stop requires JSON output. An empty object makes no approval or continuation decision.
[Console]::Out.WriteLine('{}')
exit 0
