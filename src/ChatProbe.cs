using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace GptWaitBreak
{
    /// <summary>
    /// Experimental read-only ordinary-chat detector. It reads Name only for visible Button
    /// controls, never text documents, edit fields, window titles, or conversation content.
    /// </summary>
    public static class ChatProbe
    {
        private const int MaxElements = 450;
        private const int MaxDepth = 28;
        private const int BudgetMilliseconds = 800;

        public static ChatObservation Inspect(IntPtr window, AppSettings settings)
        {
            int pid = NativeWindows.ProcessId(window);
            ChatObservation observation = new ChatObservation {
                State = ChatState.Unknown, WindowHandle = window.ToInt64(), ProcessId = pid,
                Detail = "uia:unknown"
            };
            if (settings == null || !settings.EnableUiAutomation)
            { observation.Detail = "uia:disabled"; return observation; }
            if (!NativeWindows.IsChatWindow(window, settings) || !NativeWindows.IsInspectable(window, pid))
            { observation.Detail = "uia:window-unavailable"; return observation; }

            // These are soft traversal limits. A synchronous call to a hung third-party UIA
            // provider cannot be cancelled by managed UIA. The host runs one background probe
            // at a time, so a slow provider never blocks the tray/UI thread or piles up workers.
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root == null) return observation;
                CacheRequest metadata = new CacheRequest();
                metadata.TreeScope = TreeScope.Element;
                metadata.Add(AutomationElement.ControlTypeProperty);
                metadata.Add(AutomationElement.IsOffscreenProperty);

                CacheRequest button = new CacheRequest();
                button.TreeScope = TreeScope.Element;
                button.Add(AutomationElement.NameProperty);
                button.Add(AutomationElement.IsOffscreenProperty);
                button.Add(AutomationElement.IsEnabledProperty);

                AutomationElement rootMetadata = root.GetUpdatedCache(metadata);
                if (rootMetadata.Cached.IsOffscreen || clock.ElapsedMilliseconds >= BudgetMilliseconds)
                { observation.Detail = "uia:offscreen-or-budget"; return observation; }

                List<string> names = new List<string>();
                TreeWalker walker = TreeWalker.ControlViewWalker;
                Stack<ProbeFrame> work = new Stack<ProbeFrame>();
                work.Push(new ProbeFrame(root, 0));
                int visited = 0;
                bool complete = true;
                while (work.Count != 0)
                {
                    if (visited >= MaxElements || clock.ElapsedMilliseconds >= BudgetMilliseconds)
                    { complete = false; break; }
                    ProbeFrame frame = work.Pop();
                    visited++;
                    AutomationElement element = frame.Element.GetUpdatedCache(metadata);
                    if (!element.Cached.IsOffscreen && element.Cached.ControlType == ControlType.Button)
                    {
                        AutomationElement evidence = frame.Element.GetUpdatedCache(button);
                        if (!evidence.Cached.IsOffscreen)
                        {
                            string name = evidence.Cached.Name;
                            // Empty-composer Send can legitimately be disabled. A visible Stop
                            // control, including one disabled during a transition, blocks an
                            // Idle result. Disabled approval controls are not actionable evidence.
                            if (evidence.Cached.IsEnabled || Matches(name, settings.BusyLabels)
                                || (Matches(name, settings.IdleLabels) && !Matches(name, settings.AttentionLabels))) names.Add(name);
                            if (ClassifyLabels(names, settings, false) == ChatState.Busy)
                            {
                                observation.State = NativeWindows.IsInspectable(window, pid) ? ChatState.Busy : ChatState.Unknown;
                                observation.Detail = observation.State == ChatState.Busy ? "uia:busy-button" : "uia:window-unavailable";
                                return observation;
                            }
                        }
                    }

                    if (clock.ElapsedMilliseconds >= BudgetMilliseconds)
                    { complete = false; break; }
                    // Walk in reverse document order, so composers near the bottom are usually
                    // reached before long histories. No descendant-wide FindAll allocation.
                    AutomationElement child = walker.GetLastChild(frame.Element, metadata);
                    if (child == null) continue;
                    if (frame.Depth >= MaxDepth) { complete = false; continue; }
                    List<AutomationElement> children = new List<AutomationElement>();
                    while (child != null)
                    {
                        if (visited + work.Count + children.Count >= MaxElements
                            || clock.ElapsedMilliseconds >= BudgetMilliseconds)
                        { complete = false; break; }
                        children.Add(child);
                        child = walker.GetPreviousSibling(child, metadata);
                    }
                    for (int i = children.Count - 1; i >= 0; i--)
                        work.Push(new ProbeFrame(children[i], frame.Depth + 1));
                }
                if (clock.ElapsedMilliseconds >= BudgetMilliseconds) complete = false;
                if (!NativeWindows.IsInspectable(window, pid))
                { observation.Detail = "uia:window-unavailable"; return observation; }
                observation.State = ClassifyLabels(names, settings, complete);
                observation.Detail = !complete ? "uia:incomplete" : observation.State == ChatState.Idle
                    ? "uia:idle-button" : observation.State == ChatState.NeedsUser
                    ? "uia:attention-button" : "uia:no-matching-button";
            }
            catch (ElementNotAvailableException) { observation.Detail = "uia:element-unavailable"; }
            catch (ArgumentException) { observation.Detail = "uia:window-unavailable"; }
            catch (InvalidOperationException) { observation.Detail = "uia:provider-unavailable"; }
            catch (NotSupportedException) { observation.Detail = "uia:unsupported-provider"; }
            catch (UnauthorizedAccessException) { observation.Detail = "uia:access-denied"; }
            catch (COMException) { observation.Detail = "uia:provider-error"; }
            catch (System.ComponentModel.Win32Exception) { observation.Detail = "uia:window-error"; }
            return observation;
        }

        /// <summary>Pure classifier for fixtures. Inputs are already-filtered visible button labels.</summary>
        public static ChatState ClassifyLabels(IEnumerable<string> labels, AppSettings settings, bool complete)
        {
            if (labels == null || settings == null) return ChatState.Unknown;
            bool idle = false;
            bool attention = false;
            foreach (string label in labels)
            {
                if (Matches(label, settings.BusyLabels)) return ChatState.Busy;
                if (Matches(label, settings.AttentionLabels)) attention = true;
                if (Matches(label, settings.IdleLabels)) idle = true;
            }
            // Absence of Stop, disappearance, inaccessible windows, and incomplete traversals
            // are never completion. Busy evidence wins even if a Send button is also present.
            if (!complete) return ChatState.Unknown;
            return attention ? ChatState.NeedsUser : idle ? ChatState.Idle : ChatState.Unknown;
        }

        private static bool Matches(string label, string[] configured)
        {
            string normalized = Normalize(label);
            if (normalized.Length == 0 || configured == null) return false;
            foreach (string expected in configured)
                if (string.Equals(normalized, Normalize(expected), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Normalize(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            StringBuilder result = new StringBuilder();
            bool space = false;
            foreach (char character in label.Trim())
            {
                if (char.IsWhiteSpace(character)) { space = result.Length > 0; continue; }
                if (space) result.Append(' ');
                result.Append(character);
                space = false;
            }
            return result.ToString();
        }

        private sealed class ProbeFrame
        {
            public readonly AutomationElement Element;
            public readonly int Depth;
            public ProbeFrame(AutomationElement element, int depth) { Element = element; Depth = depth; }
        }
    }
}
