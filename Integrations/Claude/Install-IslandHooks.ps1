<#
.SYNOPSIS
Adds the island observer to Claude Code hooks while preserving existing settings.
.DESCRIPTION
Uses CLAUDE_CONFIG_DIR unless a path is supplied, otherwise %USERPROFILE%\.claude.
Claude Desktop's Code tab reads the same settings.json. Re-running replaces only
handlers registered by this installer.
#>
param(
    [string]$ClaudeDirectory = $(if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$taskSettingsPath = Join-Path $ClaudeDirectory 'settings.json'
$taskUtf8 = [Text.UTF8Encoding]::new($false)
# Windows PowerShell 的 Get-Content 會把無 BOM 的 UTF-8 當成系統代碼頁，非 ASCII 設定會被弄壞。
$taskConfig = if (Test-Path -LiteralPath $taskSettingsPath) {
    [IO.File]::ReadAllText($taskSettingsPath, $taskUtf8) | ConvertFrom-Json
} else { [pscustomobject]@{} }
if ($null -eq $taskConfig) { $taskConfig = [pscustomobject]@{} }
$taskHadHooks = $null -ne $taskConfig.hooks
if (-not $taskHadHooks) {
    $taskConfig | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{}) -Force
}

# 指令寫死腳本的絕對路徑；搬動專案後要重跑安裝。
# 用腳本路徑辨識自己裝的 handler，不用 statusMessage，避免在 Claude 畫面多出提示文字。
$taskScript = Join-Path $PSScriptRoot 'Send-IslandNotification.ps1'
$taskMarker = '*\Integrations\Claude\Send-IslandNotification.ps1*'
$taskCommand = 'powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "' + $taskScript + '"'
# PreToolUse 只需要問題工具；其他工具呼叫交給 PostToolUse，減少每次開 PowerShell。
$taskEvents = [ordered]@{
    UserPromptSubmit = $null
    PreToolUse = 'AskUserQuestion'
    PostToolUse = $null
    PermissionRequest = $null
    Notification = $null
    Elicitation = $null
    Stop = $null
    StopFailure = $null
    SessionEnd = $null
}
foreach ($taskEvent in $taskEvents.Keys) {
    $taskGroups = @()
    foreach ($taskGroup in @($taskConfig.hooks.$taskEvent)) {
        if ($null -eq $taskGroup) { continue }
        $taskHandlers = @($taskGroup.hooks | Where-Object { [string]$_.command -notlike $taskMarker })
        if ($taskHandlers.Count -gt 0) {
            $taskGroup.hooks = $taskHandlers
            $taskGroups += $taskGroup
        }
    }
    if (-not $Uninstall) {
        $taskGroup = [ordered]@{}
        if ($null -ne $taskEvents[$taskEvent]) { $taskGroup.matcher = $taskEvents[$taskEvent] }
        # async 讓 hook 在背景跑，浮島沒開或 PowerShell 啟動慢都不會擋住 Claude。
        $taskGroup.hooks = @([pscustomobject][ordered]@{
            type = 'command'
            command = $taskCommand
            async = $true
            timeout = 10
        })
        $taskGroups += [pscustomobject]$taskGroup
    }
    if ($taskGroups.Count -gt 0) {
        $taskConfig.hooks | Add-Member -NotePropertyName $taskEvent -NotePropertyValue $taskGroups -Force
    } else {
        $taskConfig.hooks.PSObject.Properties.Remove($taskEvent)
    }
}
# 只移除安裝器自己建立的空 hooks；使用者原本就有的空物件保持原樣。
if (-not $taskHadHooks -and @($taskConfig.hooks.PSObject.Properties).Count -eq 0) {
    $taskConfig.PSObject.Properties.Remove('hooks')
}

# ConvertTo-Json 最多 100 層；無法完整保留原設定時，在寫入前就失敗。
# 先完整備份，再原子替換。
$taskJson = $taskConfig | ConvertTo-Json -Depth 100 -WarningAction Stop
[IO.Directory]::CreateDirectory($ClaudeDirectory) | Out-Null
$taskTemp = Join-Path $ClaudeDirectory ([IO.Path]::GetRandomFileName())
[IO.File]::WriteAllText($taskTemp, $taskJson, $taskUtf8)
if (Test-Path -LiteralPath $taskSettingsPath) {
    $taskBackup = $taskSettingsPath + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
    [IO.File]::Replace($taskTemp, $taskSettingsPath, $taskBackup)
} else {
    [IO.File]::Move($taskTemp, $taskSettingsPath)
}
Write-Output $(if ($Uninstall) { 'Island hooks removed. Other settings were preserved.' } else { 'Island hooks installed. Start a new Claude Code or Claude Desktop Code session.' })
