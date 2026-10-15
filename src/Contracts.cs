using System;
using System.Collections.Generic;

namespace GptWaitBreak
{
    public sealed class SiteEntry
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public sealed class AppSettings
    {
        public bool Enabled { get; set; }
        public int DelayMilliseconds { get; set; }
        public int StopDebounceMilliseconds { get; set; }
        public bool EnableUiAutomation { get; set; }
        public string[] ChatProcessNames { get; set; }
        public string[] BusyLabels { get; set; }
        public string[] IdleLabels { get; set; }
        public string[] AttentionLabels { get; set; }
        public string DefaultSiteUrl { get; set; }
        public List<SiteEntry> Sites { get; set; }

        public static AppSettings CreateDefault()
        {
            return new AppSettings {
                Enabled = true, DelayMilliseconds = 2000, StopDebounceMilliseconds = 900,
                EnableUiAutomation = false,
                ChatProcessNames = new[] { "ChatGPT", "Codex" },
                BusyLabels = new[] { "Stop", "Stop generating", "Stop response", "Stop streaming", "停止", "停止生成", "停止回答", "停止响应", "中止" },
                IdleLabels = new[] { "Send", "Send message", "Send prompt", "发送", "发送消息", "发送提示" },
                AttentionLabels = new[] { "Approve", "Allow once", "Allow this time", "允许一次", "批准", "允许此次" },
                DefaultSiteUrl = "https://www.bilibili.com/",
                Sites = new List<SiteEntry> {
                    new SiteEntry { Name = "B站", Url = "https://www.bilibili.com/" },
                    new SiteEntry { Name = "小红书", Url = "https://www.xiaohongshu.com/explore" }
                }
            };
        }
    }

    public sealed class BreakEvent
    {
        public string Kind { get; set; }
        public string Source { get; set; }
        public string SessionId { get; set; }
        public string TurnId { get; set; }
        public long WindowHandle { get; set; }
        public int ProcessId { get; set; }
        public long SentAtTicks { get; set; }
    }

    public enum ChatState { Unknown, Idle, Busy, NeedsUser }

    public sealed class ChatObservation
    {
        public ChatState State { get; set; }
        public long WindowHandle { get; set; }
        public int ProcessId { get; set; }
        public string Detail { get; set; }
    }
}
