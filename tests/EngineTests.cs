using System;
using GptWaitBreak;

public static class EngineTests
{
    private static int checks;

    public static int Run()
    {
        checks = 0;
        DelayAndShortAnswer();
        StopsAndContinuations();
        SessionIsolationAndOrder();
        CancellationAndTargetBinding();
        InputValidation();
        StartPreflight();
        Console.WriteLine("Break engine: " + checks + " checks passed.");
        return checks;
    }

    private static void DelayAndShortAnswer()
    {
        BreakEngine engine = new BreakEngine(2000, 900);
        engine.Receive(Event("start", "a", "1", 0), At(0));
        Action(engine.Tick(At(1999), true, true), BreakActionKind.None, "delay not reached");
        Action(engine.Tick(At(2000), true, false), BreakActionKind.None, "foreground gate");
        Action(engine.Tick(At(2100), true, true), BreakActionKind.Open, "delay opens once eligible");
        Action(engine.Tick(At(3000), true, true), BreakActionKind.None, "website opens once");

        engine = new BreakEngine(2000, 900);
        engine.Receive(Event("start", "a", "1", 0), At(0));
        engine.Receive(Event("stop", "a", "1", 100), At(100));
        Action(engine.Tick(At(999), true, true), BreakActionKind.None, "short answer waits debounce");
        Action(engine.Tick(At(1000), true, true), BreakActionKind.None, "short answer never opens or focuses");
        Check(engine.Active == null, "short answer clears active turn");
    }

    private static void StopsAndContinuations()
    {
        BreakEngine engine = Opened();
        engine.Receive(Event("stop", "a", "1", 100), At(100));
        Action(engine.Tick(At(999), true, true), BreakActionKind.None, "stop debounces");
        engine.Receive(Event("stop", "a", "1", 200), At(200));
        Action(engine.Tick(At(1000), true, true), BreakActionKind.Return, "repeated stop does not postpone");
        Check(engine.Active == null, "final stop clears active turn");

        engine = Opened();
        engine.Receive(Event("stop", "a", "1", 100), At(100));
        engine.Receive(Event("start", "a", "1", 200), At(200));
        Check(engine.Active != null && engine.Active.FinishAtTicks == 0, "new same-turn continuation cancels stop");
        Check(engine.Active.LastStartTicks == At(200), "latest continuation timestamp stored");
        engine.Receive(Event("stop", "a", "1", 100), At(250));
        Check(engine.Active.FinishAtTicks == 0, "delayed old stop cannot stop continuation");
        Action(engine.Tick(At(1500), true, true), BreakActionKind.None, "continuation remains open");
        engine.Receive(Event("stop", "a", "1", 1600), At(1600));
        Action(engine.Tick(At(2500), true, true), BreakActionKind.Return, "new stop ends continuation");

        engine = Opened();
        engine.Receive(Event("stop", "a", "1", 200), At(200));
        engine.Receive(Event("start", "a", "1", 0), At(250));
        Check(engine.Active.FinishAtTicks == At(1100), "duplicate original start cannot clear stop");
        engine.Receive(Event("start", "a", "1", 150), At(300));
        Check(engine.Active.FinishAtTicks == At(1100), "start older than terminal cannot clear stop");
        Action(engine.Tick(At(1100), true, true), BreakActionKind.Return, "out-of-order old starts do not prolong break");

        engine = new BreakEngine(2000, 900);
        engine.Receive(Event("start", "a", "1", 0), At(0));
        engine.Receive(Event("stop", "a", "1", 100), At(100));
        engine.Receive(Event("start", "a", "1", 200), At(200));
        Action(engine.Tick(At(1999), true, true), BreakActionKind.None, "continuation preserves original delay");
        Action(engine.Tick(At(2000), true, true), BreakActionKind.Open, "continued turn opens at original deadline");
    }

    private static void SessionIsolationAndOrder()
    {
        BreakEngine engine = Opened();
        engine.Receive(Event("start", "b", "2", 100), At(100));
        Check(engine.Active.SessionId == "a", "background session cannot replace active");
        engine.Receive(Event("interrupt", "b", "2", 200), At(200));
        Check(engine.Active != null, "other-session interrupt cannot return");
        engine.Receive(Event("stop", "a", "older", 200), At(200));
        Check(engine.Active.FinishAtTicks == 0, "other-turn stop cannot finish active");
        BreakEvent otherSource = Event("interrupt", "a", "1", 200);
        otherSource.Source = "uia";
        engine.Receive(otherSource, At(200));
        Check(engine.Active != null, "other-source terminal cannot finish active");

        engine = new BreakEngine(0, 900);
        engine.Receive(Event("stop", "a", "1", 200), At(200));
        engine.Receive(Event("start", "a", "1", 100), At(300));
        Check(engine.Active == null, "terminal arriving first suppresses delayed start");
        engine.Receive(Event("start", "a", "1", 250), At(350));
        Check(engine.Active != null, "newer continuation after terminal is accepted");

        engine = new BreakEngine(0, 900);
        engine.Receive(Event("session-end", "a", "", 200), At(200));
        engine.Receive(Event("start", "a", "new-turn", 100), At(300));
        Check(engine.Active == null, "session terminal suppresses delayed start of any turn");

        engine = Opened();
        Action(engine.Receive(Event("session-end", "a", "", 100), At(100)), BreakActionKind.Return,
            "turnless session-end ends matching session");
        Check(engine.Active == null, "session-end clears active");

        engine = Opened();
        engine.Receive(Event("start", "a", "1", 300), At(300));
        Action(engine.Receive(Event("session-end", "a", "", 100), At(400)), BreakActionKind.None,
            "old session-end cannot end newer continuation");
        Check(engine.Active != null, "new continuation survives stale session end");
    }

    private static void CancellationAndTargetBinding()
    {
        BreakEngine engine = Opened();
        BreakAction action = engine.Receive(Event("interrupt", "a", "1", 100), At(100));
        Action(action, BreakActionKind.Return, "interrupt returns immediately");
        Check(action.WindowHandle == 123 && action.ProcessId == 42, "return retains bound HWND and PID");
        Action(engine.Tick(At(2000), true, true), BreakActionKind.None, "interrupt returns only once");

        engine = Opened();
        Action(engine.Receive(Event("permission", "a", "1", 100), At(100)), BreakActionKind.Return,
            "permission returns immediately");

        engine = Opened();
        Action(engine.Tick(At(100), false, true), BreakActionKind.Cancel, "closed or replaced target cancels without focus");
        Check(engine.Active == null, "invalid target clears active");
        engine.Receive(Event("start", "a", "1", 0), At(200));
        Check(engine.Active == null, "duplicate start cannot revive cancelled target");

        engine = Opened();
        Action(engine.ReturnNow(), BreakActionKind.Return, "manual return");
        engine.Receive(Event("start", "a", "1", 0), At(200));
        Check(engine.Active == null, "duplicate start cannot undo manual return");

        engine = new BreakEngine(2000, 900);
        engine.Receive(Event("start", "a", "1", 0), At(0));
        Action(engine.Tick(At(100), false, true), BreakActionKind.None, "invalid target before open causes no window action");
        Check(engine.Active == null, "invalid target before open still clears active");
        engine.Receive(Event("start", "a", "2", 200), At(200));
        Check(engine.Active != null && engine.Active.TurnId == "2", "new turn can follow cancellation");
    }

    private static void InputValidation()
    {
        BreakEngine engine = new BreakEngine(0, 0);
        Action(engine.Receive(null, At(0)), BreakActionKind.None, "null event");
        BreakEvent invalid = Event("start", "a", "1", 0);
        invalid.WindowHandle = 0;
        engine.Receive(invalid, At(0));
        Check(engine.Active == null, "start requires bound window");
        invalid.WindowHandle = 123;
        invalid.ProcessId = 0;
        engine.Receive(invalid, At(0));
        Check(engine.Active == null, "start requires bound process");
        engine.Receive(Event("start", "a", "", 0), At(0));
        Check(engine.Active == null, "start requires turn identity");
        engine.Receive(Event("start", "", "1", 0), At(0));
        Check(engine.Active == null, "start requires session identity");
        engine.Receive(Event("start", "a", "1", 0), At(0));
        Action(engine.Tick(At(0), true, true), BreakActionKind.Open, "zero delay is immediate");
        engine.Receive(Event("stop", "a", "", 100), At(100));
        Check(engine.Active.FinishAtTicks == 0, "ambiguous turnless stop ignored");
        engine.Receive(Event("stop", "a", "1", 100), At(100));
        Action(engine.Tick(At(100), true, true), BreakActionKind.Return, "zero debounce is immediate on tick");
    }

    private static BreakEngine Opened()
    {
        BreakEngine engine = new BreakEngine(0, 900);
        engine.Receive(Event("start", "a", "1", 0), At(0));
        engine.Tick(At(0), true, true);
        return engine;
    }

    private static void StartPreflight()
    {
        BreakEngine engine = new BreakEngine(0, 900);
        Check(engine.CanAcceptStart(Event("start", "a", "1", 100), At(100)), "fresh start passes preflight");
        Check(engine.Active == null, "preflight does not create Active");
        Check(!engine.CanAcceptStart(null, At(100)), "preflight rejects null");
        Check(!engine.CanAcceptStart(Event("stop", "a", "1", 100), At(100)), "preflight accepts start kind only");
        BreakEvent invalid = Event("start", "a", "1", 100);
        invalid.WindowHandle = 0;
        Check(!engine.CanAcceptStart(invalid, At(100)), "preflight requires window binding");
        invalid.WindowHandle = 123;
        invalid.ProcessId = 0;
        Check(!engine.CanAcceptStart(invalid, At(100)), "preflight requires PID binding");

        engine.Receive(Event("stop", "a", "old", 200), At(200));
        Check(!engine.CanAcceptStart(Event("start", "a", "old", 100), At(300)), "preflight checks turn tombstone");
        Check(engine.CanAcceptStart(Event("start", "a", "old", 250), At(300)), "new continuation after tombstone allowed");
        engine.Receive(Event("session-end", "b", "", 200), At(200));
        Check(!engine.CanAcceptStart(Event("start", "b", "any", 100), At(300)), "preflight checks session barrier");

        engine.Receive(Event("start", "a", "current", 300), At(300));
        ActiveBreak before = engine.Active;
        engine.Receive(Event("stop", "a", "current", 350), At(350));
        long finish = engine.Active.FinishAtTicks;
        Check(!engine.CanAcceptStart(Event("start", "a", "older-turn", 200), At(400)), "preflight rejects stale different turn before replacement");
        Check(!engine.CanAcceptStart(Event("start", "a", "current", 300), At(400)), "preflight rejects active start duplicate");
        Check(!engine.CanAcceptStart(Event("start", "a", "current", 320), At(400)), "preflight rejects start older than latest stop");
        Check(engine.CanAcceptStart(Event("start", "another-session", "new-turn", 400), At(400)), "preflight permits newer candidate without replacement policy");
        Check(Object.ReferenceEquals(engine.Active, before) && engine.Active.FinishAtTicks == finish,
            "preflight preserves active object and pending stop");
        engine.Receive(Event("start", "a", "older-turn", 200), At(400));
        Check(Object.ReferenceEquals(engine.Active, before), "Receive also ignores stale other-turn start");

        engine = new BreakEngine(0, 900);
        for (int i = 0; i < 258; i++) engine.Receive(Event("stop", "history", i.ToString(), 200), At(200));
        Check(!engine.CanAcceptStart(Event("start", "history", "256", 100), At(300)),
            "bounded history preserves recent tombstones rather than clearing all entries");
    }

    private static BreakEvent Event(string kind, string session, string turn, int milliseconds)
    {
        return new BreakEvent { Kind = kind, Source = "hooks", SessionId = session, TurnId = turn,
            WindowHandle = 123, ProcessId = 42, SentAtTicks = At(milliseconds) };
    }

    private static long At(int milliseconds)
    { return TimeSpan.FromDays(1).Ticks + (long)milliseconds * TimeSpan.TicksPerMillisecond; }

    private static void Action(BreakAction action, BreakActionKind expected, string message)
    { Check(action.Kind == expected, message + ": expected " + expected + ", got " + action.Kind); }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
