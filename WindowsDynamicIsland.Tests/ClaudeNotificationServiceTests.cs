using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WindowsDynamicIsland.Models;
using WindowsDynamicIsland.Services;

internal static class ClaudeNotificationServiceTests
{
    public static async Task<int> RunAsync()
    {
        foreach (var malformed in new[] { "{", "[]", "null", "{\"session_id\":5}", "{\"session_id\":\"test\",\"hook_event_name\":{}}",
            "{\"session_id\":\"test\",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"Bash\"}",
            "{\"session_id\":\"test\",\"hook_event_name\":\"Notification\",\"notification_type\":\"auth_success\"}" })
            Check(ClaudeNotificationService.ParseNotification(malformed) is null, "ignore malformed, unknown, or unhandled input");

        var working = Parse("UserPromptSubmit");
        Check(working is { Source: "Claude", IsVisualizerActive: true, RequiresAttention: false }, "working starts visualizer");
        Check(Parse("PostToolUse") == working, "tool completion keeps working status");
        Check(Parse("PreToolUse", tool: "AskUserQuestion") is { RequiresAttention: true, RequestId: null }, "question needs attention");
        var permission = Parse("PermissionRequest");
        Check(permission.RequiresAttention, "permission needs attention");
        Check(Parse("Notification", notificationType: "permission_prompt") == permission, "permission notification matches permission request");
        Check(Parse("Notification", notificationType: "elicitation_dialog").RequiresAttention, "elicitation dialog needs attention");
        Check(Parse("Elicitation").RequiresAttention, "MCP elicitation needs attention");
        Check(Parse("Notification", notificationType: "idle_prompt") is { RequiresAttention: false, IsVisualizerActive: false }, "idle prompt is informational");
        Check(Parse("Stop") is { RequiresAttention: false, IsVisualizerActive: false }, "response ready clears attention");
        var failure = Parse("StopFailure", errorType: "rate_limit");
        Check(failure.RequiresAttention && failure.Message.Contains("rate_limit"), "stop failure shows error type");
        Check(Parse("SessionEnd").Title == "Claude session ended", "session end");

        var queue = new AgentNotificationQueue();
        queue.Update(permission);
        queue.Update(working with { SessionId = "other" });
        Check(queue.Current == permission, "other sessions do not hide pending approval");
        queue.Update(Parse("PostToolUse"));
        Check(queue.Current?.RequiresAttention == false, "approved tool clears the same session approval");

        // 使用隔離的 pipe 與設定目錄，不會動到真正的 Claude 設定。
        var pipeName = "WindowsDynamicIsland.Tests." + Guid.NewGuid().ToString("N");
        using var service = new ClaudeNotificationService(pipeName);
        var received = new TaskCompletionSource<AgentNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.NotificationRaised += (_, notification) => received.TrySetResult(notification);
        service.Start();
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync();
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            await writer.WriteAsync("{\"session_id\":\"test\",\"hook_event_name\":\"PermissionRequest\"}");
        }
        Check((await received.Task) is { Source: "Claude", RequiresAttention: true }, "real named pipe delivers hook event");
        service.Dispose();

        var integration = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Integrations/Claude"));
        var installer = Path.Combine(integration, "Install-IslandHooks.ps1");
        var fixture = Path.Combine(Path.GetTempPath(), "IslandClaudeHooksTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var settingsPath = Path.Combine(fixture, "settings.json");
            const string original = "{\"model\":\"opus\",\"env\":{\"NOTE\":\"中文設定\"},\"permissions\":{\"allow\":[\"Bash(git status)\"],\"deny\":[]}," +
                "\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo original\"}]}]," +
                "\"PreToolUse\":[{\"matcher\":\"Bash\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo guard\"}]}]}}";
            await File.WriteAllTextAsync(settingsPath, original, new UTF8Encoding(false));
            for (var install = 0; install < 2; install++) // 裝兩次，確認不會重複加入。
                await PowerShellAsync(installer, null, "-ClaudeDirectory", fixture);
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)))
            {
                var root = config.RootElement;
                Check(root.GetProperty("model").GetString() == "opus", "installer preserves other settings");
                Check(root.GetProperty("env").GetProperty("NOTE").GetString() == "中文設定", "installer preserves non-ASCII settings");
                Check(root.GetProperty("permissions").GetProperty("allow").GetArrayLength() == 1, "installer preserves single-item arrays");
                Check(root.GetProperty("permissions").GetProperty("deny").GetArrayLength() == 0, "installer preserves empty arrays");
                var hooks = root.GetProperty("hooks");
                Check(hooks.GetProperty("Stop").GetArrayLength() == 2, "installer preserves existing hook without duplicating observer");
                var preToolUse = hooks.GetProperty("PreToolUse");
                Check(preToolUse.GetArrayLength() == 2 && preToolUse[1].GetProperty("matcher").GetString() == "AskUserQuestion",
                    "question observer uses a matcher");
                var handler = hooks.GetProperty("Notification")[0].GetProperty("hooks")[0];
                Check(handler.GetProperty("async").GetBoolean() && handler.GetProperty("command").GetString()!.Contains("Send-IslandNotification.ps1"),
                    "observer runs asynchronously");
            }
            Check(Directory.GetFiles(fixture, "settings.json.*.bak").Length == 2, "installer backs up settings before each write");
            await PowerShellAsync(installer, null, "-ClaudeDirectory", fixture, "-Uninstall");
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)))
            {
                var hooks = config.RootElement.GetProperty("hooks");
                Check(hooks.GetProperty("Stop").GetArrayLength() == 1 && hooks.GetProperty("PreToolUse").GetArrayLength() == 1,
                    "uninstall preserves original handlers");
                Check(!hooks.TryGetProperty("Notification", out _), "uninstall removes empty observer events");
            }

            var emptyFixture = Path.Combine(fixture, "new");
            await PowerShellAsync(installer, null, "-ClaudeDirectory", emptyFixture);
            await PowerShellAsync(installer, null, "-ClaudeDirectory", emptyFixture, "-Uninstall");
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(emptyFixture, "settings.json"))))
                Check(!config.RootElement.GetProperty("hooks").EnumerateObject().Any(), "install and uninstall leave no observer events");

            // 使用者原本就有的空 hooks 物件要保留，解除安裝後回到原樣。
            var emptyHooksFixture = Path.Combine(fixture, "empty-hooks");
            Directory.CreateDirectory(emptyHooksFixture);
            const string emptyHooks = "{\"hooks\":{},\"model\":\"opus\"}";
            await File.WriteAllTextAsync(Path.Combine(emptyHooksFixture, "settings.json"), emptyHooks);
            await PowerShellAsync(installer, null, "-ClaudeDirectory", emptyHooksFixture);
            await PowerShellAsync(installer, null, "-ClaudeDirectory", emptyHooksFixture, "-Uninstall");
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(emptyHooksFixture, "settings.json"))))
                Check(config.RootElement.TryGetProperty("hooks", out var kept) && !kept.EnumerateObject().Any()
                    && config.RootElement.GetProperty("model").GetString() == "opus", "uninstall keeps an existing empty hooks object");

            var sender = Path.Combine(integration, "Send-IslandNotification.ps1");
            Check(await PowerShellAsync(sender, "invalid JSON") == string.Empty, "hook prints nothing on invalid input");
            Check(await PowerShellAsync(sender, "{\"session_id\":\"test\",\"hook_event_name\":\"Stop\"}") == string.Empty,
                "hook prints nothing when the island is closed");
        }
        finally { Directory.Delete(fixture, recursive: true); }
        Console.WriteLine("Claude notification regressions passed.");
        return 0;
    }

    private static AgentNotification Parse(string eventName, string tool = "", string notificationType = "", string errorType = "") =>
        ClaudeNotificationService.ParseNotification(JsonSerializer.Serialize(new
        {
            session_id = "test",
            hook_event_name = eventName,
            tool_name = tool,
            notification_type = notificationType,
            error_type = errorType
        }))!;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }

    private static async Task<string> PowerShellAsync(string script, string? input, params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        if (input is not null) await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
}
