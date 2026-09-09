using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WindowsDynamicIsland.Models;
using WindowsDynamicIsland.Services;

internal static class CodexNotificationServiceTests
{
    public static async Task<int> RunAsync()
    {
        foreach (var malformed in new[] { "{", "[]", "null", "{\"session_id\":5}", "{\"session_id\":\"test\",\"hook_event_name\":{}}" })
            Check(CodexNotificationService.ParseNotification(malformed) is null, "ignore malformed or unknown input");

        var working = Parse("UserPromptSubmit");
        Check(working is { Source: "Codex", IsVisualizerActive: true, RequiresAttention: false }, "working starts visualizer");
        var permission = Parse("PermissionRequest");
        Check(permission.RequiresAttention && permission.RequestId is null, "permission remains informational and persistent");
        Check(Parse("PreToolUse", "request_user_input").RequiresAttention, "question needs attention");
        Check(Parse("Stop") is { IsVisualizerActive: false, RequiresAttention: false }, "response ready clears attention");
        Check(Parse("Interrupt").Title == "Codex interrupted", "interrupt is not success");
        Check(Parse("SessionEnd").Title == "Codex session ended", "session end");

        var queue = new AgentNotificationQueue();
        var openCodeQuestion = new AgentNotification("Question", "Choose", "test", "", true, RequestId: "q", Options: ["A"]);
        queue.Update(openCodeQuestion);
        queue.Update(working);
        Check(queue.Current == openCodeQuestion, "Codex cannot overwrite an OpenCode question with the same session id");
        queue.ResolveQuestion("Codex", "q");
        Check(queue.Current == openCodeQuestion, "question resolution is provider scoped");
        queue.ResolveQuestion("OpenCode", "q");
        Check(queue.Current == working, "queued activity survives attention resolution");
        queue.Dismiss(working);
        queue.Update(working);
        Check(queue.Current is null, "repeated tool hooks do not reopen dismissed working status");
        queue.Update(permission);
        queue.Update(working with { SessionId = "other" });
        Check(queue.Current == permission, "other sessions do not hide pending approval");
        queue.Update(Parse("Stop"));
        Check(queue.Current?.RequiresAttention == false, "same session can clear its approval");

        queue = new AgentNotificationQueue();
        queue.Update(permission);
        queue.Update(openCodeQuestion);
        Check(queue.Current == openCodeQuestion, "answerable question precedes informational approval");
        queue.Update(openCodeQuestion with { Title = "Working", RequiresAttention = false, RequestId = null, Options = null });
        Check(queue.Current == openCodeQuestion, "same-session background status preserves unanswered question");
        queue.Update(openCodeQuestion with { RequestId = "q2" });
        Check(queue.Current == openCodeQuestion, "later questions do not preempt the current question");
        queue.ResolveQuestion("OpenCode", "q");
        Check(queue.Current?.RequestId == "q2", "resolving current question exposes next pending question");

        // Use an isolated pipe and config directory. No real Codex settings or tasks are changed.
        var pipeName = "WindowsDynamicIsland.Tests." + Guid.NewGuid().ToString("N");
        using var service = new CodexNotificationService(pipeName);
        var received = new TaskCompletionSource<AgentNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.NotificationRaised += (_, notification) => received.TrySetResult(notification);
        service.Start();
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync();
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            await writer.WriteAsync("{\"session_id\":\"test\",\"hook_event_name\":\"PermissionRequest\"}");
        }
        Check((await received.Task).RequiresAttention, "real named pipe delivers hook event");
        service.Dispose();

        var integration = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Integrations/Codex"));
        var fixture = Path.Combine(Path.GetTempPath(), "IslandHooksTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var hooksPath = Path.Combine(fixture, "hooks.json");
            const string original = "{\"description\":\"keep\",\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo original\"}]}]}}";
            await File.WriteAllTextAsync(hooksPath, original);
            for (var install = 0; install < 2; install++) // A second install proves idempotency.
                await PowerShellAsync(Path.Combine(integration, "Install-IslandHooks.ps1"), null, "-CodexDirectory", fixture);
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(hooksPath)))
            {
                Check(config.RootElement.GetProperty("description").GetString() == "keep", "installer preserves metadata");
                Check(config.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength() == 2, "installer preserves existing hook without duplicating observer");
            }
            await PowerShellAsync(Path.Combine(integration, "Install-IslandHooks.ps1"), null, "-CodexDirectory", fixture, "-Uninstall");
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(hooksPath)))
                Check(config.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength() == 1, "uninstall preserves original handler");
            var output = await PowerShellAsync(Path.Combine(integration, "Send-IslandNotification.ps1"), "invalid JSON");
            Check(output.Trim() == "{}", "hook always returns neutral JSON, even on invalid input");
        }
        finally { Directory.Delete(fixture, recursive: true); }
        Console.WriteLine("Codex notification regressions passed.");
        return 0;
    }

    private static AgentNotification Parse(string eventName, string toolName = "") =>
        CodexNotificationService.ParseNotification(JsonSerializer.Serialize(new { session_id = "test", hook_event_name = eventName, tool_name = toolName }))!;

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
