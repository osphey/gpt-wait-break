using System;
using System.Collections.Generic;

namespace GptWaitBreak
{
    public enum BreakActionKind { None, Open, Return, Cancel }
    public sealed class BreakAction
    {
        public BreakActionKind Kind { get; set; }
        public long WindowHandle { get; set; }
        public int ProcessId { get; set; }
        public string Reason { get; set; }
        public static BreakAction None() { return new BreakAction { Kind = BreakActionKind.None }; }
    }

    public sealed class ActiveBreak
    {
        public string SessionId { get; set; }
        public string TurnId { get; set; }
        public string Source { get; set; }
        public long WindowHandle { get; set; }
        public int ProcessId { get; set; }
        public long OpenAtTicks { get; set; }
        public long FinishAtTicks { get; set; }
        public long LastStartTicks { get; set; }
        public bool Shown { get; set; }
    }

    // Pure state machine: no hooks, UI, clocks or browser APIs inside it.
    public sealed class BreakEngine
    {
        private readonly Dictionary<string, long> terminalEvents = new Dictionary<string, long>();
        private readonly List<string> terminalOrder = new List<string>();
        private const int TerminalHistoryLimit = 256;
        public ActiveBreak Active { get; private set; }
        public int DelayMilliseconds { get; set; }
        public int StopDebounceMilliseconds { get; set; }

        public BreakEngine(int delayMilliseconds, int stopDebounceMilliseconds)
        {
            DelayMilliseconds = delayMilliseconds;
            StopDebounceMilliseconds = stopDebounceMilliseconds;
        }

        private static string Field(string value) { return (value ?? String.Empty).Length + ":" + value; }
        private static string Key(string source, string session, string turn)
        { return "turn:" + Field(source) + Field(session) + Field(turn); }
        private static string Key(BreakEvent e) { return Key(e.Source, e.SessionId, e.TurnId); }
        private static string SessionKey(BreakEvent e)
        { return "session:" + Field(e.Source) + Field(e.SessionId); }
        private bool Matches(BreakEvent e)
        {
            return Active != null && e.Source == Active.Source && e.SessionId == Active.SessionId
                && (e.TurnId == Active.TurnId || (e.Kind == "session-end" && String.IsNullOrEmpty(e.TurnId)));
        }

        // Pure preflight for hosts that may cancel/replace an existing turn. This deliberately
        // does not decide foreground or source priority, and does not mutate history or Active.
        public bool CanAcceptStart(BreakEvent e, long now)
        {
            if (e == null || e.Kind != "start" || String.IsNullOrEmpty(e.Source)
                || String.IsNullOrEmpty(e.SessionId) || String.IsNullOrEmpty(e.TurnId)
                || e.WindowHandle == 0 || e.ProcessId <= 0) return false;
            long sent = e.SentAtTicks > 0 ? e.SentAtTicks : now;
            long terminal;
            if ((terminalEvents.TryGetValue(Key(e), out terminal) && sent <= terminal)
                || (terminalEvents.TryGetValue(SessionKey(e), out terminal) && sent <= terminal)) return false;
            return Active == null || sent > Active.LastStartTicks;
        }

        public BreakAction Receive(BreakEvent e, long now)
        {
            if (e == null || String.IsNullOrEmpty(e.Source) || String.IsNullOrEmpty(e.SessionId)) return BreakAction.None();
            long sent = e.SentAtTicks > 0 ? e.SentAtTicks : now;
            if (e.Kind == "start") {
                if (!CanAcceptStart(e, now)) return BreakAction.None();
                if (Active != null) {
                    if (Matches(e) && sent > Active.LastStartTicks) {
                        Active.LastStartTicks = sent;
                        Active.FinishAtTicks = 0;
                    }
                    // A background task must not replace the chat the user is waiting for.
                    return BreakAction.None();
                }
                Active = new ActiveBreak { SessionId = e.SessionId, TurnId = e.TurnId, Source = e.Source,
                    WindowHandle = e.WindowHandle, ProcessId = e.ProcessId,
                    LastStartTicks = sent,
                    OpenAtTicks = now + MillisecondTicks(DelayMilliseconds) };
                return BreakAction.None();
            }
            if (e.Kind == "stop" || e.Kind == "interrupt" || e.Kind == "permission" || e.Kind == "session-end") {
                // A session-end without a turn also suppresses a delayed start for any older
                // turn in that session. Missing-turn stop/interrupt/permission are ambiguous.
                if (String.IsNullOrEmpty(e.TurnId) && e.Kind != "session-end") return BreakAction.None();
                RecordTerminal(e.Kind == "session-end" && String.IsNullOrEmpty(e.TurnId) ? SessionKey(e) : Key(e), sent);
                if (!Matches(e)) return BreakAction.None();
                // Delivery can race across hook processes. A terminal emitted before a newer
                // continuation must never end that continuation, even if it arrives afterwards.
                if (sent < Active.LastStartTicks) return BreakAction.None();
                if (e.Kind == "stop") {
                    // Repeated stop events cannot postpone return forever.
                    if (Active.FinishAtTicks == 0) Active.FinishAtTicks = now + MillisecondTicks(StopDebounceMilliseconds);
                    return BreakAction.None();
                }
                return Finish(BreakActionKind.Return, e.Kind);
            }
            return BreakAction.None();
        }

        public BreakAction Tick(long now, bool targetValid, bool mayOpen)
        {
            if (Active == null) return BreakAction.None();
            if (!targetValid) return Finish(BreakActionKind.Cancel, "target-closed");
            if (Active.FinishAtTicks > 0) {
                if (now >= Active.FinishAtTicks) return Finish(BreakActionKind.Return, "stop");
                return BreakAction.None();
            }
            if (!Active.Shown && mayOpen && now >= Active.OpenAtTicks) {
                Active.Shown = true;
                return new BreakAction { Kind = BreakActionKind.Open, WindowHandle = Active.WindowHandle, ProcessId = Active.ProcessId };
            }
            return BreakAction.None();
        }

        public BreakAction Cancel(string reason) { return Finish(BreakActionKind.Cancel, reason); }
        public BreakAction ReturnNow() { return Finish(BreakActionKind.Return, "manual"); }

        private BreakAction Finish(BreakActionKind kind, string reason)
        {
            if (Active == null) return BreakAction.None();
            ActiveBreak old = Active;
            // Cancellation/manual return also tombstones the current start, so its duplicate
            // cannot reopen a website after the user returns or the target closes.
            RecordTerminal(Key(old.Source, old.SessionId, old.TurnId), old.LastStartTicks);
            Active = null;
            return new BreakAction { Kind = old.Shown ? kind : BreakActionKind.None,
                WindowHandle = old.WindowHandle, ProcessId = old.ProcessId, Reason = reason };
        }

        private static long MillisecondTicks(int milliseconds)
        { return (long)Math.Max(0, milliseconds) * TimeSpan.TicksPerMillisecond; }

        private void RecordTerminal(string key, long ticks)
        {
            long old;
            if (terminalEvents.TryGetValue(key, out old) && ticks <= old) return;
            terminalEvents[key] = ticks;
            terminalOrder.Remove(key);
            terminalOrder.Add(key);
            if (terminalOrder.Count > TerminalHistoryLimit) {
                string oldest = terminalOrder[0];
                terminalOrder.RemoveAt(0);
                terminalEvents.Remove(oldest);
            }
        }
    }
}
