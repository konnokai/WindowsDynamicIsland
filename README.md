# WindowsDynamicIsland

Windows 桌面浮島，可顯示媒體、電量及 OpenCode / Codex / Claude 通知。

## 建置

需要 Windows、.NET 8 SDK 及 Windows App SDK 工具。

```powershell
dotnet build -c Release -p:Platform=x64
dotnet run -c Debug -p:Platform=x64
```

## Codex 通知

在專案目錄執行一次安裝，將通知 hook 加入目前使用者的 Codex 設定：

```powershell
powershell.exe -NoProfile -File .\Integrations\Codex\Install-IslandHooks.ps1
```

安裝程式使用 `CODEX_HOME`，未設定時使用 `%USERPROFILE%\.codex`。它會保留既有 hooks，並在更新前備份 `hooks.json`。搬動專案後請重新執行安裝。

依 Codex 提示檢閱並信任新增的 hooks，再開始新的回合。需要支援 [Codex hooks](https://learn.chatgpt.com/docs/hooks) 的本機版本；遠端與雲端工作不會傳送到這台電腦。

- 開始處理與工具執行時，顯示白色 Agent 圖示及工作中提示。
- 權限請求與問題工具呼叫時，顯示需要回應的提示。請回到 Codex 核准或回答。
- `Stop` 顯示「回覆已就緒」，不代表程式、測試或整個工作已成功；其他 hooks 仍可能要求繼續工作。
- 中斷或工作階段結束時，顯示對應提示。
- 一般通知沿用 4 秒自動收回；需要回應的通知會保留，也能按叉號關閉。其他工作階段的一般活動不會蓋掉它。

hooks 只透過目前 Windows 使用者的本機 named pipe 傳送事件名稱、工作階段 ID 與工具名稱，不讀取對話記錄、不傳送提示詞或工具內容，也不代替使用者核准。浮島未執行時直接略過通知，不累積歷史通知。Codex hooks 沒有通用的執行失敗事件，因此不將工具結束誤判為整個工作成功或失敗；異步問題與平行工具的等待狀態仍以 Codex 畫面為準。

移除整合：

```powershell
powershell.exe -NoProfile -File .\Integrations\Codex\Install-IslandHooks.ps1 -Uninstall
```

本機回歸測試：

```powershell
dotnet run --project WindowsDynamicIsland.Tests -c Release -- --codex
```

## Claude 通知

支援 Claude Code CLI 與 Claude Desktop 的 Code 分頁（兩者共用同一份 hooks 設定）。Claude Desktop 一般聊天沒有 hooks，不會通知。

在專案目錄執行一次安裝：

```powershell
powershell.exe -NoProfile -File .\Integrations\Claude\Install-IslandHooks.ps1
```

安裝程式寫入 `CLAUDE_CONFIG_DIR\settings.json`，未設定時寫入 `%USERPROFILE%\.claude\settings.json`。它會保留其他設定與既有 hooks，更新前備份成 `settings.json.<guid>.bak`。重跑不會重複加入。搬動專案後請重新執行安裝。裝好後開新的 Claude 工作階段才會生效。

- 送出提示與工具執行結束時，顯示工作中提示。
- 權限請求、`AskUserQuestion` 問題、MCP 輸入請求時，顯示需要回應的提示。請回到 Claude 核准或回答。
- `Stop` 顯示「回覆已就緒」，不代表整個工作成功。`StopFailure` 顯示錯誤類型（例如 `rate_limit`），並保留到你關閉。
- 閒置等待輸入、工作階段結束時，顯示對應提示。

hook 以 `async` 背景執行，不會拖慢 Claude，也不輸出任何內容到對話。只透過目前 Windows 使用者的本機 named pipe 傳送事件名稱、工作階段 ID、工具名稱、通知類型與錯誤類型；不傳送提示詞、工具參數、通知訊息或對話記錄路徑，也不代替使用者核准。浮島未執行時直接略過。

移除整合：

```powershell
powershell.exe -NoProfile -File .\Integrations\Claude\Install-IslandHooks.ps1 -Uninstall
```

本機回歸測試（只用暫存目錄，不動真實設定）：

```powershell
dotnet run --project WindowsDynamicIsland.Tests -c Release -- --claude
```

## 圖示來源與授權

Agent 通知使用 `Assets/agent_icon.png`，為依下列原圖製作、由使用者提供的白色透明背景版本。

- 原圖：[Agents](https://www.flaticon.com/free-icon/agents_11681328)
- 作者：[Cap Cool](https://www.flaticon.com/authors/cap-cool)
- 平台：Flaticon
- 授權：[Flaticon License](https://www.flaticon.com/legal)，原圖頁標示可供個人與商業用途使用，但須註明出處。
- 修改：改為白色並移除背景，用於深色通知介面。

**Agent icons created by Cap Cool - Flaticon** ([出處連結](https://www.flaticon.com/free-icons/agent))。

此圖示沿用原素材的授權條件，不因收入本專案而轉為其他授權。散布應用程式時請一併保留此 README 的署名與來源說明。
