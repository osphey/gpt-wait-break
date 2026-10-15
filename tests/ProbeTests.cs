using System;
using GptWaitBreak;

public static class ProbeTests
{
    public static int Run()
    {
        AppSettings settings = AppSettings.CreateDefault();
        int count = 0;
        Check(ChatState.Unknown, new string[0], settings, true, "no buttons is not completion"); count++;
        Check(ChatState.Unknown, new[] { "Copy", "Share" }, settings, true, "unrelated labels"); count++;
        Check(ChatState.Unknown, new[] { "Stop recording", "Send feedback" }, settings, true, "exact match only"); count++;
        Check(ChatState.Busy, new[] { "Send", "Stop generating" }, settings, true, "busy outranks idle"); count++;
        Check(ChatState.Busy, new[] { "Approve", "Stop" }, settings, true, "busy outranks attention"); count++;
        Check(ChatState.Busy, new[] { "停止生成" }, settings, false, "positive busy with incomplete tree"); count++;
        Check(ChatState.Idle, new[] { "  sEnD\t\r\n message  " }, settings, true, "case and whitespace normalization"); count++;
        Check(ChatState.Idle, new[] { "发送消息" }, settings, true, "Chinese idle"); count++;
        Check(ChatState.Unknown, new[] { "Send" }, settings, false, "incomplete tree cannot finish"); count++;
        Check(ChatState.NeedsUser, new[] { "Send", "Allow once" }, settings, true, "attention outranks idle"); count++;
        Check(ChatState.Unknown, new[] { "Allow once" }, settings, false, "incomplete tree cannot request return"); count++;
        Check(ChatState.Unknown, null, settings, true, "null evidence"); count++;
        Check(ChatState.Unknown, new[] { "Stop" }, null, true, "null settings"); count++;
        settings.BusyLabels = new[] { "Pause job" };
        settings.IdleLabels = null;
        settings.AttentionLabels = null;
        Check(ChatState.Unknown, new[] { "Stop", "Send" }, settings, true, "configured aliases replace defaults"); count++;
        Check(ChatState.Busy, new[] { " pause  job " }, settings, true, "custom aliases normalized"); count++;
        settings.BusyLabels = new[] { "", null, "  " };
        Check(ChatState.Unknown, new[] { "", null, "  " }, settings, true, "empty aliases never match"); count++;
        Console.WriteLine("Probe classifier: " + count + " checks passed.");
        return count;
    }

    private static void Check(ChatState expected, string[] labels, AppSettings settings, bool complete, string reason)
    {
        ChatState actual = ChatProbe.ClassifyLabels(labels, settings, complete);
        if (actual != expected) throw new Exception(reason + ": expected " + expected + ", got " + actual);
    }
}
