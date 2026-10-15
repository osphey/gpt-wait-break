using System;
using System.IO;
using System.Threading;
using GptWaitBreak;

public static class TestRunner
{
    public static int Main(string[] args)
    {
        try {
            if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("GPT_WAIT_BREAK_DATA_DIR"))) throw new Exception("Run tests/run.ps1 to isolate all test data.");
            if (args.Length > 1 && args[0] == "--ipc-fixture") {
                using (ManualResetEvent ready = new ManualResetEvent(false))
                using (EventTransport server = new EventTransport(delegate(BreakEvent e) {
                    File.WriteAllText(args[1], Configuration.Json.Serialize(e)); ready.Set();
                })) { server.Start(); bool received = ready.WaitOne(10000); if (received) Thread.Sleep(250); return received ? 0 : 1; }
            }
            int count = EngineTests.Run() + ProbeTests.Run();
            count += CheckConfigurationAndHooks();
            count += CheckTransport();
            Console.WriteLine("Total: " + count + " checks passed.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int CheckConfigurationAndHooks()
    {
        int count = 0;
        AppSettings settings = AppSettings.CreateDefault();
        Configuration.Validate(settings); count++;
        foreach (string bad in new[] { "javascript:alert(1)", "file:///C:/Windows", "https://name:password@example.com", "", "not a url" }) {
            Assert(!Configuration.IsWebUrl(bad), "reject unsafe URL: " + bad); count++;
        }
        Assert(Configuration.IsWebUrl("https://example.com/path?q=test"), "accept valid custom website"); count++;
        settings.Sites.Add(new SiteEntry { Name = "Custom", Url = "https://example.com/" });
        settings.DefaultSiteUrl = "https://example.com/";
        Configuration.Save(settings);
        AppSettings restored = Configuration.Load();
        Assert(restored.DefaultSiteUrl == settings.DefaultSiteUrl && restored.Sites.Count == settings.Sites.Count, "configuration round trip"); count++;
        restored.DelayMilliseconds = -1;
        bool rejected = false;
        try { Configuration.Save(restored); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected && Configuration.Load().DelayMilliseconds >= 0, "invalid save preserves last good configuration"); count++;
        BreakEvent e = HookInput.Parse("UserPromptSubmit", "{\"session_id\":\"fixture-session\",\"turn_id\":\"fixture-turn\",\"prompt\":\"SECRET_FIXTURE\",\"transcript_path\":\"PRIVATE_PATH\",\"tool_input\":{\"text\":\"SECRET_TOOL\"}}");
        string sanitized = Configuration.Json.Serialize(e);
        Assert(e.Kind == "start" && e.Source == "hook" && e.SentAtTicks > 0, "hook normalized with original emission timestamp"); count++;
        Assert(sanitized.IndexOf("SECRET", StringComparison.Ordinal) < 0 && sanitized.IndexOf("PRIVATE", StringComparison.Ordinal) < 0, "hook strips prompt/transcript/tool payload"); count++;
        Assert(HookInput.Parse("SessionEnd", "{\"session_id\":\"fixture\"}").Kind == "session-end", "session end accepts absent turn ID"); count++;
        foreach (string bad in new[] { "null", "{}", "{\"session_id\":12,\"turn_id\":\"t\"}", "{\"session_id\":\"s\",\"turn_id\":\"bad\\nID\"}" }) {
            rejected = false;
            try { HookInput.Parse("Stop", bad); } catch { rejected = true; }
            Assert(rejected, "malformed hook input rejected"); count++;
        }
        Console.WriteLine("Configuration and privacy: " + count + " checks passed.");
        return count;
    }

    private static int CheckTransport()
    {
        // The runner always uses an isolated GPT_WAIT_BREAK_DATA_DIR/pipe namespace.
        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("GPT_WAIT_BREAK_DATA_DIR"))) throw new Exception("Test isolation is required.");
        BreakEvent observed = null;
        int count = 0;
        using (ManualResetEvent delivered = new ManualResetEvent(false))
        using (EventTransport server = new EventTransport(delegate(BreakEvent e) { observed = e; delivered.Set(); })) {
            server.Start();
            for (int iteration = 0; iteration < 25; iteration++) {
                delivered.Reset();
                BreakEvent message = new BreakEvent { Kind = "stop", Source = "hook", SessionId = "ipc-fixture", TurnId = "turn-fixture-" + iteration, SentAtTicks = 42 + iteration };
                bool sent = false;
                for (int i = 0; i < 15 && !sent; i++) { sent = EventTransport.Send(message, false); if (!sent) Thread.Sleep(30); }
                Assert(sent && delivered.WaitOne(2000), "private named-pipe delivery acknowledged on iteration " + iteration); count++;
                Assert(observed != null && observed.SessionId == "ipc-fixture" && observed.SentAtTicks == 42 + iteration, "IPC preserves normalized correlation and timestamp"); count++;
            }
        }
        Console.WriteLine("IPC: " + count + " checks passed.");
        return count;
    }

    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
}
