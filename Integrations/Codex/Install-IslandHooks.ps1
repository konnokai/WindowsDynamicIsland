<#
.SYNOPSIS
Adds the island observer to Codex hooks while preserving existing hook handlers.
.DESCRIPTION
Uses the active CODEX_HOME unless a path is supplied. Re-running replaces only
handlers registered by this installer. The user still reviews hook trust in Codex.
#>
param(
    [string]$CodexDirectory = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$taskHooksPath = Join-Path $CodexDirectory 'hooks.json'
$taskMarker = 'Windows Dynamic Island: Codex status'
$taskConfig = if (Test-Path -LiteralPath $taskHooksPath) {
    Get-Content -LiteralPath $taskHooksPath -Raw | ConvertFrom-Json
} else { [pscustomobject]@{} }
if ($null -eq $taskConfig.hooks) {
    $taskConfig | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{}) -Force
}

# Keep the script at its reviewed absolute location; relocating the repository
# requires re-running this installer so Codex can review the new command.
$taskScript = Join-Path $PSScriptRoot 'Send-IslandNotification.ps1'
$taskCommand = 'powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "' + $taskScript + '"'
$taskEvents = 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'PermissionRequest', 'Stop', 'Interrupt', 'SessionEnd'
foreach ($taskEvent in $taskEvents) {
    $taskGroups = @()
    foreach ($taskGroup in @($taskConfig.hooks.$taskEvent)) {
        if ($null -eq $taskGroup) { continue }
        $taskHandlers = @($taskGroup.hooks | Where-Object { $_.statusMessage -ne $taskMarker })
        if ($taskHandlers.Count -gt 0) {
            $taskGroup.hooks = $taskHandlers
            $taskGroups += $taskGroup
        }
    }
    if (-not $Uninstall) {
        $taskGroups += [pscustomobject]@{ hooks = @([pscustomobject]@{
            type = 'command'
            command = $taskCommand
            commandWindows = $taskCommand
            statusMessage = $taskMarker
        }) }
    }
    $taskConfig.hooks | Add-Member -NotePropertyName $taskEvent -NotePropertyValue $taskGroups -Force
}

# ConvertTo-Json supports at most 100 levels; fail before writing if that cannot
# preserve the original config. Keep an exact backup and atomically replace it.
$taskJson = $taskConfig | ConvertTo-Json -Depth 100 -WarningAction Stop
[IO.Directory]::CreateDirectory($CodexDirectory) | Out-Null
$taskTemp = Join-Path $CodexDirectory ([IO.Path]::GetRandomFileName())
[IO.File]::WriteAllText($taskTemp, $taskJson, [Text.UTF8Encoding]::new($false))
if (Test-Path -LiteralPath $taskHooksPath) {
    $taskBackup = $taskHooksPath + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
    [IO.File]::Replace($taskTemp, $taskHooksPath, $taskBackup)
} else {
    [IO.File]::Move($taskTemp, $taskHooksPath)
}
Write-Output $(if ($Uninstall) { 'Island hooks removed. Other hooks were preserved.' } else { 'Island hooks installed. Review and trust the added hooks in Codex, then start a new turn.' })
