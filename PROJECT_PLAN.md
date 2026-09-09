# Windows Dynamic Island

## 目標

建立一個 Windows 桌面常駐應用程式，以 WinUI 3 顯示接近 iPhone Dynamic Island 的黑色膠囊，平常維持低干擾，發生活動時提供簡短狀態與必要控制。

## 已確認決策

- 平台：Windows 10 22H2 以上、Windows 11。
- 技術：.NET / C#、WinUI 3、Windows App SDK。
- 首版視覺：接近 iPhone 的純黑膠囊。
- 未來視覺：可切換 Windows Fluent 風格；首版先用集中式色彩與圓角資源，避免把樣式散落在程式碼中。
- 首發核心：媒體播放控制。
- 首發輔助功能：電池與電源提示、基本設定、系統匣常駐；系統音量服務已排除。
- 長期方向：以類似插件的方式擴充活動與通知來源，可接入 OpenCode、Codex 等狀態通知。
- 目前已接入 OpenCode/OpenChamber SSE 與 Codex 本機 hooks；插件系統與帳號同步仍不實作。

## 首版範圍

### 必須完成

1. 無標題列、不可調整大小、置頂的浮動膠囊視窗。
2. 以螢幕工作區為基準定位在頂部中央。
3. 不因被動更新而搶走其他程式焦點。
4. 收合與展開兩種基本狀態。
5. 讀取目前 Windows Global System Media Transport Controls 工作階段。
6. 顯示媒體標題、作者、播放狀態與播放/暫停控制。
7. 顯示電池電量與電源狀態；無電池裝置不顯示電池區塊。
8. 系統匣入口：顯示/隱藏、設定入口預留、退出。
9. 沒有活動通知時將膠囊收回螢幕上緣；需要顯示活動時再下拉。

### 暫不完成

- 讀取所有應用程式通知。
- 插件載入、插件沙箱、插件市場或遠端同步。
- 完整的 OpenCode/Codex 插件協定；目前提供 OpenCode/OpenChamber SSE 與 Codex 本機 hooks adapter。
- 系統音量事件與自製音量提示；依目前決策先排除，不作為首版重點。
- 多螢幕獨立浮島與複雜工作區同步。

## 架構

先維持單一 WinUI 3 應用程式，按職責分資料夾，不提前拆類別庫。

```text
WindowsDynamicIsland/
  App.xaml(.cs)             啟動與視窗生命週期
  MainWindow.xaml(.cs)      浮島畫面與 Win32 視窗設定
  Models/                   顯示狀態資料
  Services/                 Windows 系統 API 的薄封裝
  ViewModels/               UI 狀態與命令
```

目前活動資料流：

```text
Windows API -> Service -> ViewModel -> WinUI 3 View
```

未來若要做插件，插件應該只提供活動資料與操作命令，不直接操作視窗或 XAML。現階段不建立插件介面；先讓媒體與電源服務保持獨立，作為未來自然的切入點。

## 實作順序

1. 建立 unpackaged WinUI 3 專案與最小啟動流程。
2. 完成黑色膠囊視窗：邊框、置頂、定位、展開/收合、滑鼠操作。
3. 接入媒體工作階段，處理工作階段不存在、媒體資料讀取失敗與播放狀態更新。
4. 接入電池與電源狀態。
5. 加入系統匣與基本隱藏/退出操作。
6. 在 Windows 10 22H2 與 Windows 11 驗證 DPI、螢幕變更、焦點與視窗行為。

## 驗收條件

- `dotnet build` 可在 x64 Release 成功完成且無編譯錯誤。
- 啟動後可看到頂部中央的黑色膠囊。
- 點擊膠囊可展開與收合，不會造成位置跳動。
- 沒有可用媒體工作階段時，畫面仍可正常啟動並顯示合理的空狀態。
- 可控制支援 GSMTC 的媒體播放器播放/暫停。
- 被動刷新狀態不主動搶焦點。
- 系統匣可退出程式。

## 目前進度

- [x] 確認工作目錄目前為空。
- [x] 確認本機 .NET SDK 為 10.0.400。
- [x] 確認 `dotnet new list` 沒有 WinUI 3 專用模板。
- [x] 建立專案檔與 WinUI 3 原始碼。
- [x] 完成第一個可建置垂直切片。
- [x] x64 Release 建置成功，0 警告、0 錯誤。
- [x] 本機啟動 smoke test 成功，程序可正常存活。
- [x] 加入系統匣右鍵退出入口與左鍵啟用視窗。
- [x] 預設將膠囊收回螢幕上緣，媒體展開時下拉顯示。
- [x] 依決策移除系統音量服務與音量 UI。
- [x] 修正 GSMTC/電源事件從非 UI 執行緒更新 binding 導致的 `0x8001010E`。
- [x] 媒體 Snapshot 更新時自動下拉展開；媒體工作階段消失時收回。
- [x] 接入 OpenCode/OpenChamber 本機 SSE 通知：工作中、完成、錯誤、權限與問題狀態。
- [x] OpenCode 通知面板具備優先級與自動收回；需要注意的通知保持顯示。
- [x] 加入 Codex hooks 通知、跨來源注意事項排序，以及共用白色 Agent 圖示；設定與素材授權見 README.md。
- [x] Agent 通知高度依內容量測並包含外層 padding，避免第二行文字與字母下緣遭固定高度裁切。
- [x] Agent 通知移除右側音訊柱，音訊視覺效果僅保留於媒體面板。
- [x] 修正 OpenChamber SSE 外層 `payload` 包裝格式，確保實際事件能被解析。
- [x] 支援 `question.asked` 顯示選項，選擇後回傳 `question.replied`。
- [x] 對齊實際 OpenChamber question 格式：使用 `properties.id` 與巢狀 `answers`。
- [x] 加入膠囊尺寸/位置的 easing 轉場，收回與下拉不再瞬移。
- [x] 加入 OpenCode working 狀態的 Apple 風格 5 柱 voice-style visualizer。
- [x] 將 media visualizer 改為靜音 1px 中心線，系統音訊增加時由中心向上下兩側對稱展開。
- [x] 移除 media panel 與 collapsed panel 的喇叭圖示。
- [x] 無電池裝置不建立電源 snapshot，因此媒體資訊下方不顯示電量。
- [x] 加入 1024 點 FFT 與 5 段頻譜分析，visualizer 各線依低頻到高頻獨立反應。
- [x] 修正 WASAPI 32-bit `Extensible` 浮點格式解碼與頻段增益，避免能量接近 0。
- [x] 補強 FFT 輸入格式判斷與 RMS fallback，避免非標準 loopback 格式造成頻段全為 0。

## 實作備註

- Windows App SDK 使用 `1.8.260804001`，以取得較完整的 CLI XAML 編譯診斷。
- 目前專案為 unpackaged WinUI 3，目標 `net8.0-windows10.0.19041.0`，x64。
- 目前沒有自行維護插件介面；媒體與電源服務分離，避免為尚未存在的外部通知協定增加抽象層。
- 尚未做 Windows 10 22H2 實機驗證；目前只完成本機 Windows 環境的建置與啟動 smoke test。
- 系統匣提供暫時隱藏、恢復與退出；暫時隱藏持續到手動恢復。左鍵恢復不搶焦點，全螢幕避讓仍優先。
- 同螢幕前景視窗的 client area 覆蓋完整螢幕時自動隱藏，離開全螢幕後恢復；使用 WinEvent 監聽前景與位置變更，不輪詢。
- 有 request ID 的待回答問題優先於一般注意事項，同工作階段的背景狀態不會清除問題。短暫提示結束後恢復音樂原本的展開或收合狀態；提示更新不延長既有倒數。
- WASAPI 在預設輸出裝置變更、裝置出現/移除或擷取停止時重建 loopback；舊擷取先停止，再清除頻譜緩衝，關閉後不接受延遲重連。
- 系統音量事件曾以 Core Audio COM interop 嘗試，但因啟動穩定性與首版優先級不符已移除；不要在後續 session 自動恢復。
- 視窗收回/下拉使用 220ms easing 轉場，同步插值尺寸與位置；尚未加入更複雜的通知排程器。
- voice-style visualizer 使用 NAudio WASAPI loopback 擷取預設系統輸出混音，計算五段 FFT 頻譜，不儲存或傳送音訊；OpenCode `session.status=busy` 與媒體播放會觸發顯示。
- visualizer 的每根線都以 `RenderTransformOrigin=0.5,0.5` 從垂直中心縮放；無聲音時 baseline 為 0.03，約等於 1px 中心線。
- 音樂條採一段低頻、三段中頻與一段常見高頻：60-250、250-500、500-1000、1000-2000、2000-6000 Hz；使用 Hann window 與 50% overlap，各頻段上界不含，避免重複採用邊界 bin。這是本專案的視覺調校，非 Apple 的頻段規格。
- NAudio forward FFT 已完成取樣點數正規化，不再重複除以 1024；各頻段使用 peak 與既有 32 倍顯示增益，再套用平方根曲線增加弱訊號可見度，保留各段獨立起伏與靜音零值，不使用 RMS 填平。
- 系統音訊擷取失敗時會靜默降級為中心線，不影響浮島主流程；FFT 使用 50% overlap 持續更新 5 段頻譜。
- 新增 `NAudio 2.2.1`，使用 `WasapiLoopbackCapture`，不需要麥克風錄音權限。
- `PowerService` 遇到 `BatteryStatus.NotPresent` 會發布 `null`，桌機不會顯示誤導性的電量資訊。
- 系統事件更新先經由 ViewModel 的 `DispatcherQueue` 回到 XAML UI 執行緒，再觸發 `PropertyChanged`。
- `MainWindow` 監聽 `MediaSnapshot` 的 UI 更新，將新媒體活動轉成 `IsExpanded` 狀態，避免只有文字更新而視窗仍藏在上緣。
- OpenCode/OpenChamber 通知預設嘗試 `http://127.0.0.1:57123/api/global/event` 與 `http://127.0.0.1:4096/global/event`；可用 `OPENCHAMBER_HOST` 或 `OPENCODE_HOST` 指定主機。
- OpenCode SSE 監聽器為背景重連服務，連線失敗不影響浮島啟動；通知正常狀態 4 秒後收回，需要注意的權限/問題狀態不自動收回。
- OpenChamber wrapped `payload.session.status` smoke test 已確認通知可觸發下拉，視窗 `Top=12`；一般啟動 smoke test 亦通過。
- `question.asked` 選項透過 `POST /question/{requestID}/reply` 回傳；OpenChamber host 會使用 `/api/question/{requestID}/reply` proxy，其他 SSE client 可收到伺服器廣播的 `question.replied`。
- 本機 question 整合測試已實際點擊選項，捕獲回傳 body `{"answers":[["Alpha"]]}`，並確認程序持續執行。
- 膠囊外層 HWND 尺寸需包含 `Border.Padding`；目前收合尺寸為 244x80、展開尺寸為 516x96，內部面板分別為 228x64、500x80。
- 展開面板已放寬標題欄並隱藏 WinUI 預設標題列內容，避免長曲名與控制按鈕互相擠壓。

## 後續 session 起始指令

1. 先讀取本文件與工作樹狀態。
2. 只處理尚未完成的目前階段，不提前實作插件框架；Codex 目前範圍為本機 hooks 提示。
3. 變更後執行最小相關建置或測試，並更新本文件的「目前進度」。
