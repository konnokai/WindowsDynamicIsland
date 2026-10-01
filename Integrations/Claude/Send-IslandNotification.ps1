# Claude Code 從 stdin 傳入一個 JSON 物件。只轉送事件中繼資料，不轉送提示詞、
# 工具參數、訊息內容或對話記錄路徑。這個 hook 只觀察，不做任何決定。
$ErrorActionPreference = 'Stop'
$taskPipe = $null
$taskWriter = $null
try {
    $taskEvent = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $taskPayload = @{
        hook_event_name = [string]$taskEvent.hook_event_name
        session_id = [string]$taskEvent.session_id
        tool_name = [string]$taskEvent.tool_name
        notification_type = [string]$taskEvent.notification_type
        error_type = [string]$taskEvent.error_type
    } | ConvertTo-Json -Compress
    $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskPipe = [IO.Pipes.NamedPipeClientStream]::new('.', "WindowsDynamicIsland.Claude.$taskSid", [IO.Pipes.PipeDirection]::Out)
    # 浮島沒開時不等待，避免拖慢 Claude。
    $taskPipe.Connect(0)
    $taskWriter = [IO.StreamWriter]::new($taskPipe, [Text.UTF8Encoding]::new($false))
    $taskWriter.Write($taskPayload)
    $taskWriter.Flush()
} catch {
    # 浮島沒開、寫入中斷或輸入格式錯誤，都不能影響 Claude。
} finally {
    if ($null -ne $taskWriter) { $taskWriter.Dispose() }
    if ($null -ne $taskPipe) { $taskPipe.Dispose() }
}
# 不輸出任何 stdout：UserPromptSubmit 和 Stop 的純文字輸出會被加進 Claude 的對話內容。
exit 0
