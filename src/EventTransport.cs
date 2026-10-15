using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace GptWaitBreak
{
    public static class HookInput
    {
        private const int MaximumInputCharacters = 2097152;
        public static BreakEvent Parse(string eventName, string json)
        {
            if (json == null || json.Length > MaximumInputCharacters) throw new InvalidDataException("Hook payload too large.");
            var parser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = MaximumInputCharacters, RecursionLimit = 40 };
            Dictionary<string, object> raw = parser.Deserialize<Dictionary<string, object>>(json);
            if (raw == null) throw new InvalidDataException("Missing hook object.");
            string kind;
            switch (eventName) {
                case "UserPromptSubmit": kind = "start"; break;
                case "Stop": kind = "stop"; break;
                case "Interrupt": kind = "interrupt"; break;
                case "PermissionRequest": kind = "permission"; break;
                case "SessionEnd": kind = "session-end"; break;
                default: throw new InvalidDataException("Unsupported hook event.");
            }
            string sessionId = ReadId(raw, "session_id");
            string turnId = ReadId(raw, "turn_id");
            if (String.IsNullOrEmpty(sessionId) || (kind != "session-end" && String.IsNullOrEmpty(turnId)))
                throw new InvalidDataException("Hook event has no session/turn identifier.");
            // Deliberately exclude prompt, transcript_path, cwd, tool inputs, and assistant text.
            return new BreakEvent { Kind = kind, Source = "hook", SessionId = sessionId,
                TurnId = turnId, SentAtTicks = DateTime.UtcNow.Ticks };
        }

        private static string ReadId(Dictionary<string, object> raw, string key)
        {
            object value;
            if (!raw.TryGetValue(key, out value) || value == null) return "";
            string text = value as string;
            if (text == null || text.Length > 200 || text.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidDataException("Invalid event identifier.");
            return text;
        }

        public static string ReadInput(TextReader reader)
        {
            StringBuilder builder = new StringBuilder();
            char[] buffer = new char[4096];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) {
                if (builder.Length + count > MaximumInputCharacters) throw new InvalidDataException("Hook payload too large.");
                builder.Append(buffer, 0, count);
            }
            return builder.ToString();
        }
    }

    public sealed class EventTransport : IDisposable
    {
        private volatile bool stopping;
        private NamedPipeServerStream currentPipe;
        private readonly object gate = new object();
        private readonly Action<BreakEvent> received;
        public EventTransport(Action<BreakEvent> received) { this.received = received; }

        public void Start()
        {
            Thread thread = new Thread(Serve) { IsBackground = true, Name = "WaitBreak IPC" };
            thread.Start();
        }

        private void Serve()
        {
            while (!stopping) {
                try {
                    PipeSecurity security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (NamedPipeServerStream pipe = new NamedPipeServerStream(AppPaths.PipeName,
                        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                        lock (gate) { if (stopping) return; currentPipe = pipe; }
                        pipe.WaitForConnection();
                        // A bad same-user client cannot hold the only endpoint indefinitely.
                        using (System.Threading.Timer timeout = new System.Threading.Timer(delegate { try { pipe.Dispose(); } catch { } }, null, 1000, Timeout.Infinite))
                        using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8)) {
                            string line = ReadBoundedLine(reader, 16384);
                            BreakEvent message = Configuration.Json.Deserialize<BreakEvent>(line);
                            if (message != null) {
                                received(message);
                                pipe.WriteByte(6); // ACK before the sender closes its pipe handle.
                                pipe.Flush();
                            }
                        }
                    }
                } catch (IOException) { }
                  catch (ObjectDisposedException) { }
                  catch (ArgumentException) { }
                  catch (InvalidOperationException) { }
                  catch (UnauthorizedAccessException) { Thread.Sleep(300); }
                finally { lock (gate) { currentPipe = null; } }
            }
        }

        private static string ReadBoundedLine(TextReader reader, int limit)
        {
            StringBuilder text = new StringBuilder();
            int character;
            while ((character = reader.Read()) != -1 && character != '\n') {
                if (text.Length >= limit) throw new InvalidDataException("Event too large.");
                text.Append((char)character);
            }
            return text.ToString();
        }

        public static bool Send(BreakEvent message, bool autoStart)
        {
            if (TrySend(message, 120)) return true;
            if (!autoStart) return false;
            Process.Start(new ProcessStartInfo(AppPaths.Executable, "--run") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 1500) {
                if (TrySend(message, 80)) return true;
                Thread.Sleep(30);
            }
            return false;
        }

        private static bool TrySend(BreakEvent message, int milliseconds)
        {
            try {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", AppPaths.PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification)) {
                    pipe.Connect(milliseconds);
                    using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false))) {
                        writer.WriteLine(Configuration.Json.Serialize(message));
                        writer.Flush();
                        // Without an ACK, a fast WinExe can exit before the server accepts/reads
                        // the connection; Windows can then report a broken pipe and lose the event.
                        byte[] acknowledgement = new byte[1];
                        var read = pipe.ReadAsync(acknowledgement, 0, 1);
                        if (!read.Wait(150) || read.Result != 1 || acknowledgement[0] != 6) return false;
                    }
                }
                return true;
            } catch (TimeoutException) { return false; }
              catch (IOException) { return false; }
              catch (UnauthorizedAccessException) { return false; }
              catch (AggregateException) { return false; }
        }

        public void Dispose()
        {
            stopping = true;
            lock (gate) { if (currentPipe != null) { try { currentPipe.Dispose(); } catch { } } }
        }
    }
}
