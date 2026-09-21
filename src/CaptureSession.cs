using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ThorEvidence.Core
{
    public enum CaptureState
    {
        Idle,
        OpeningPort,
        RecordingBeforePower,
        WaitingForNvShell,
        WaitingForPrompt,
        CollectingReadOnlyCommands,
        WatchingPowerTransition,
        Completed,
        Stopped,
        Faulted
    }

    public sealed class CaptureSession : IDisposable
    {
        private readonly EvidenceParser _parser = new EvidenceParser();
        private readonly List<string> _events = new List<string>();
        private readonly List<string> _extraWarnings = new List<string>();
        private readonly List<string> _attemptedCommands = new List<string>();
        private readonly List<string> _timedOutCommands = new List<string>();
        private readonly HashSet<string> _sentCommands = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _pendingCommands = new Queue<string>();
        private readonly Func<byte[], bool> _sender;
        private readonly UTF8Encoding _utf8 = new UTF8Encoding(false, false);
        private readonly Decoder _decoder;
        private readonly char[] _decodeBuffer = new char[65536];
        private FileStream _rawStream;
        private StreamWriter _transcript;
        private StringBuilder _currentCommandOutput;
        private string _currentCommand;
        private DateTime _currentCommandStartedUtc;
        private DateTime _currentCommandDeadlineUtc;
        private DateTime _nextCommandDueUtc;
        private DateTime _firstRxUtc;
        private DateTime _powerdownUtc;
        private bool _crlfSentForCycle;
        private bool _commandsInitializedForCycle;
        private int _handledBootCycles;
        private bool _active;
        private bool _exported;
        private int _rawBytes;
        private string _stopReason = "";

        public CaptureSession(Func<byte[], bool> sender, bool synthetic)
        {
            _sender = sender;
            Synthetic = synthetic;
            _decoder = _utf8.GetDecoder();
            State = CaptureState.Idle;
            _powerdownUtc = DateTime.MinValue;
        }

        public event Action<string, string> Log;
        public event Action<CaptureState> StateChanged;
        public event Action<EvidenceSnapshot> SnapshotChanged;
        public event Action<string> Exported;

        public EvidenceParser Parser { get { return _parser; } }
        public CaptureState State { get; private set; }
        public bool Synthetic { get; private set; }
        public bool IsActive { get { return _active; } }
        public bool IsExported { get { return _exported; } }
        public string SessionDirectory { get; private set; }
        public string RawPath { get; private set; }
        public string TranscriptPath { get; private set; }
        public string CommandsDirectory { get; private set; }
        public string PortName { get; private set; }
        public string PortCaption { get; private set; }
        public int BaudRate { get; private set; }
        public DateTime StartedUtc { get; private set; }
        public DateTime LastRxUtc { get; private set; }
        public int RawBytes { get { return _rawBytes; } }
        public string LastExportPath { get; private set; }

        public void Start(string sessionDirectory, string portName, string portCaption, int baudRate)
        {
            if (_active)
            {
                throw new InvalidOperationException("A capture session is already active.");
            }

            SessionDirectory = Path.GetFullPath(sessionDirectory);
            Directory.CreateDirectory(SessionDirectory);
            CommandsDirectory = Path.Combine(SessionDirectory, "commands");
            Directory.CreateDirectory(CommandsDirectory);
            RawPath = Path.Combine(SessionDirectory, "raw-rx.bin");
            TranscriptPath = Path.Combine(SessionDirectory, "serial-transcript.log");
            PortName = portName ?? "";
            PortCaption = portCaption ?? "";
            BaudRate = baudRate;
            StartedUtc = DateTime.UtcNow;
            LastRxUtc = DateTime.MinValue;
            _rawBytes = 0;
            _firstRxUtc = DateTime.MinValue;
            _powerdownUtc = DateTime.MinValue;
            _handledBootCycles = 0;
            _crlfSentForCycle = false;
            _commandsInitializedForCycle = false;
            _sentCommands.Clear();
            _pendingCommands.Clear();
            _events.Clear();
            _extraWarnings.Clear();
            _attemptedCommands.Clear();
            _timedOutCommands.Clear();
            _currentCommand = null;
            _currentCommandOutput = null;
            _stopReason = "";
            _exported = false;
            LastExportPath = "";

            _rawStream = new FileStream(RawPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, FileOptions.WriteThrough);
            _transcript = new StreamWriter(TranscriptPath, false, _utf8);
            _transcript.AutoFlush = true;
            WriteTranscript("INFO", "SESSION START synthetic=" + Synthetic + " port=" + PortName + " baud=" + BaudRate + " 8N1 flow=none");
            WriteSessionMetadata(false, "");
            _active = true;
            SetState(CaptureState.RecordingBeforePower);
            AddEvent("recording started; raw capture is active before any transmit");
            EmitLog("INFO", "正在录制。现在可以给设备上电；程序不会发送上电/复位命令。", true);
        }

        public void ProcessReceived(byte[] bytes)
        {
            if (!_active || bytes == null || bytes.Length == 0)
            {
                return;
            }

            _rawStream.Write(bytes, 0, bytes.Length);
            _rawStream.Flush();
            _rawBytes += bytes.Length;
            DateTime now = DateTime.UtcNow;
            if (_firstRxUtc == DateTime.MinValue) _firstRxUtc = now;
            LastRxUtc = now;

            // SerialPort normally supplies small chunks, but replay can pass a
            // complete multi-megabyte log. Decode in bounded pieces so a large
            // input cannot overflow the fixed char buffer.
            int offset = 0;
            while (offset < bytes.Length)
            {
                int chunkLength = Math.Min(8192, bytes.Length - offset);
                int count = _decoder.GetChars(bytes, offset, chunkLength, _decodeBuffer, 0, false);
                offset += chunkLength;
                if (count <= 0) continue;

                string text = new String(_decodeBuffer, 0, count);
                ProcessDecodedText(text, now);
            }

            NotifySnapshot();
        }

        private void ProcessDecodedText(string text, DateTime now)
        {
            _parser.Append(text);
            WriteTranscript("RX", text);
            EmitLog("RX", text, false);
            HandleParserProgress(now);

            if (_currentCommand != null)
            {
                _currentCommandOutput.Append(text);
                if (_currentCommandOutput.ToString().IndexOf("NvShell>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FinishCurrentCommand(false);
                }
            }
        }

        public void Tick()
        {
            Tick(DateTime.UtcNow);
        }

        public void Tick(DateTime nowUtc)
        {
            if (!_active)
            {
                return;
            }

            EvidenceSnapshot snapshot = Snapshot();
            if (_firstRxUtc != DateTime.MinValue && nowUtc >= _firstRxUtc.AddSeconds(90))
            {
                Complete("90-second capture window elapsed");
                return;
            }

            if (snapshot.PowerDownRequested && _powerdownUtc == DateTime.MinValue)
            {
                _powerdownUtc = nowUtc;
                AddEvent("power-down transition observed; quiet period started");
                SetState(CaptureState.WatchingPowerTransition);
            }

            if (_currentCommand != null && nowUtc >= _currentCommandDeadlineUtc)
            {
                FinishCurrentCommand(true);
            }

            if (_currentCommand == null && _pendingCommands.Count > 0 && nowUtc >= _nextCommandDueUtc)
            {
                SendNextCommand(nowUtc);
            }

            snapshot = Snapshot();
            if (_currentCommand == null && _pendingCommands.Count == 0 && snapshot.CommandsAttempted.Count > 0 &&
                snapshot.PowerDownRequested && _powerdownUtc != DateTime.MinValue && nowUtc >= _powerdownUtc.AddSeconds(3))
            {
                Complete("read-only queue finished and power-down quiet period elapsed");
            }

            NotifySnapshot();
        }

        public void StopAndExport(string reason)
        {
            if (!_active && _exported)
            {
                return;
            }

            _stopReason = String.IsNullOrEmpty(reason) ? "manual stop" : reason;
            SetState(CaptureState.Stopped);
            FinalizeAndExport(true);
        }

        public void FailAndExport(string reason)
        {
            _stopReason = String.IsNullOrEmpty(reason) ? "capture fault" : reason;
            AddWarning("capture fault: " + _stopReason);
            SetState(CaptureState.Faulted);
            FinalizeAndExport(true);
        }

        public EvidenceSnapshot Snapshot()
        {
            EvidenceSnapshot snapshot = _parser.Snapshot();
            for (int i = 0; i < _events.Count; i++) snapshot.AddEvent(_events[i]);
            for (int i = 0; i < _extraWarnings.Count; i++) snapshot.AddWarning(_extraWarnings[i]);
            for (int i = 0; i < _attemptedCommands.Count; i++)
            {
                if (!snapshot.CommandsAttempted.Contains(_attemptedCommands[i])) snapshot.CommandsAttempted.Add(_attemptedCommands[i]);
            }
            for (int i = 0; i < _timedOutCommands.Count; i++)
            {
                if (!snapshot.CommandsTimedOut.Contains(_timedOutCommands[i])) snapshot.CommandsTimedOut.Add(_timedOutCommands[i]);
            }
            if (_currentCommand != null && !snapshot.CommandsAttempted.Contains(_currentCommand)) snapshot.CommandsAttempted.Add(_currentCommand);
            return snapshot;
        }

        public void AddEvent(string message)
        {
            if (!String.IsNullOrEmpty(message))
            {
                _events.Add(DateTime.UtcNow.ToString("o") + " " + message);
                EmitLog("EVENT", message, true);
            }
        }

        public void AddWarning(string warning)
        {
            if (!String.IsNullOrEmpty(warning) && !_extraWarnings.Contains(warning))
            {
                _extraWarnings.Add(warning);
                EmitLog("WARN", warning, true);
            }
        }

        private void HandleParserProgress(DateTime nowUtc)
        {
            EvidenceSnapshot snapshot = _parser.Snapshot();
            if (snapshot.BootCycleCount > _handledBootCycles)
            {
                if (_currentCommand != null)
                {
                    FinishCurrentCommand(true);
                }
                _handledBootCycles = snapshot.BootCycleCount;
                _crlfSentForCycle = false;
                _commandsInitializedForCycle = false;
                _sentCommands.Clear();
                _pendingCommands.Clear();
                AddEvent("new NvShell boot cycle " + _handledBootCycles);
                SetState(CaptureState.WaitingForPrompt);
            }

            bool promptHint = Regex.IsMatch(_parser.Text, "Press.{0,120}Enter.{0,120}NvShell\\s+prompt|NvShell\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!_crlfSentForCycle && promptHint)
            {
                if (SendCrlf())
                {
                    _crlfSentForCycle = true;
                    AddEvent("safe CRLF sent after NvShell prompt hint");
                    SetState(CaptureState.WaitingForPrompt);
                }
            }

            if (_crlfSentForCycle && snapshot.PromptSeen && !_commandsInitializedForCycle)
            {
                _commandsInitializedForCycle = true;
                _pendingCommands.Clear();
                string[] commands = CommandPolicy.AllowedCommands;
                for (int i = 0; i < commands.Length; i++) _pendingCommands.Enqueue(commands[i]);
                _nextCommandDueUtc = nowUtc;
                AddEvent("read-only queue armed");
                SetState(CaptureState.CollectingReadOnlyCommands);
            }
        }

        private bool SendCrlf()
        {
            return SendBytes(new byte[] { 13, 10 }, "TX [safe CRLF]");
        }

        private void SendNextCommand(DateTime nowUtc)
        {
            string command = _pendingCommands.Dequeue();
            if (!CommandPolicy.IsAllowed(command) || _sentCommands.Contains(command))
            {
                AddWarning("internal command policy prevented transmit: " + command);
                _nextCommandDueUtc = nowUtc.AddMilliseconds(1200);
                return;
            }

            byte[] bytes = _utf8.GetBytes(CommandPolicy.ToWireCommand(command));
            if (!SendBytes(bytes, "TX [read-only] " + command))
            {
                AddWarning("command transmit failed: " + command);
                _nextCommandDueUtc = nowUtc.AddMilliseconds(1200);
                return;
            }

            _sentCommands.Add(command);
            if (!_attemptedCommands.Contains(command)) _attemptedCommands.Add(command);
            _currentCommand = command;
            _currentCommandStartedUtc = nowUtc;
            _currentCommandDeadlineUtc = nowUtc.AddSeconds(6);
            _currentCommandOutput = new StringBuilder();
            SetState(CaptureState.CollectingReadOnlyCommands);
        }

        private void FinishCurrentCommand(bool timeout)
        {
            if (_currentCommand == null)
            {
                return;
            }

            string fileName = String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:000}-{1}.txt", _sentCommands.Count, SafeFileName(_currentCommand));
            string commandPath = Path.Combine(CommandsDirectory, fileName);
            string output = _currentCommandOutput == null ? "" : _currentCommandOutput.ToString();
            if (timeout)
            {
                output = output + Environment.NewLine + "[collector: TIMEOUT after 6000 ms]" + Environment.NewLine;
            }
            File.WriteAllText(commandPath, output, _utf8);
            EvidenceSnapshot snapshot = Snapshot();
            if (!_attemptedCommands.Contains(_currentCommand)) _attemptedCommands.Add(_currentCommand);
            if (timeout && !_timedOutCommands.Contains(_currentCommand)) _timedOutCommands.Add(_currentCommand);
            AddEvent("command " + _currentCommand + (timeout ? " timed out" : " completed"));
            if (timeout) AddWarning("command timed out: " + _currentCommand);

            _currentCommand = null;
            _currentCommandOutput = null;
            _nextCommandDueUtc = DateTime.UtcNow.AddMilliseconds(1200);
            NotifySnapshot();
        }

        private bool SendBytes(byte[] bytes, string transcriptLabel)
        {
            if (_sender == null)
            {
                AddWarning("no serial transport is attached; transmit skipped");
                return false;
            }

            bool sent;
            try
            {
                sent = _sender(bytes);
            }
            catch (Exception ex)
            {
                AddWarning("serial transmit exception: " + ex.Message);
                return false;
            }

            if (sent)
            {
                WriteTranscript("TX", transcriptLabel);
                EmitLog("TX", transcriptLabel, false);
            }
            return sent;
        }

        private void Complete(string reason)
        {
            if (!_active) return;
            _stopReason = reason;
            SetState(CaptureState.Completed);
            AddEvent("capture completed: " + reason);
            FinalizeAndExport(false);
        }

        private void FinalizeAndExport(bool partial)
        {
            if (_exported) return;
            _active = false;
            try
            {
                if (_currentCommand != null)
                {
                    FinishCurrentCommand(true);
                }
            }
            catch (Exception ex)
            {
                AddWarning("failed to finalize active command: " + ex.Message);
            }

            try
            {
                WriteTranscript("INFO", "SESSION END partial=" + partial + " reason=" + (_stopReason ?? ""));
                if (_rawStream != null) { _rawStream.Flush(); _rawStream.Dispose(); _rawStream = null; }
                if (_transcript != null) { _transcript.Flush(); _transcript.Dispose(); _transcript = null; }
                WriteSessionMetadata(partial, _stopReason);
                LastExportPath = EvidenceExporter.Export(this, partial, _stopReason);
                _exported = true;
                EmitLog("EXPORT", "已导出: " + LastExportPath, true);
                if (Exported != null) Exported(LastExportPath);
            }
            catch (Exception ex)
            {
                AddWarning("export failed: " + ex.Message);
                try
                {
                    if (_rawStream != null) { _rawStream.Dispose(); _rawStream = null; }
                    if (_transcript != null) { _transcript.Dispose(); _transcript = null; }
                }
                catch { }
            }
        }

        private void WriteSessionMetadata(bool partial, string reason)
        {
            if (String.IsNullOrEmpty(SessionDirectory)) return;
            StringBuilder json = new StringBuilder();
            bool first = true;
            json.Append('{');
            JsonUtil.Property(json, ref first, "schemaVersion", JsonUtil.StringValue("1.0"));
            JsonUtil.Property(json, ref first, "startedUtc", JsonUtil.StringValue(StartedUtc.ToString("o")));
            JsonUtil.Property(json, ref first, "endedUtc", JsonUtil.StringValue(DateTime.UtcNow.ToString("o")));
            JsonUtil.Property(json, ref first, "synthetic", JsonUtil.BoolValue(Synthetic));
            JsonUtil.Property(json, ref first, "partial", JsonUtil.BoolValue(partial));
            JsonUtil.Property(json, ref first, "reason", JsonUtil.StringValue(reason));
            JsonUtil.Property(json, ref first, "port", JsonUtil.StringValue(PortName));
            JsonUtil.Property(json, ref first, "caption", JsonUtil.StringValue(PortCaption));
            JsonUtil.Property(json, ref first, "baud", JsonUtil.IntValue(BaudRate));
            json.Append('}');
            File.WriteAllText(Path.Combine(SessionDirectory, "session.json"), json.ToString(), _utf8);
        }

        private void WriteTranscript(string kind, string text)
        {
            if (_transcript == null) return;
            string safe = text ?? "";
            safe = safe.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = safe.Split(new char[] { '\n' });
            if (lines.Length == 0) lines = new string[] { "" };
            for (int i = 0; i < lines.Length; i++)
            {
                _transcript.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + kind + "] " + lines[i]);
            }
        }

        private void EmitLog(string kind, string text, bool visibleAsLine)
        {
            if (Log != null) Log(kind, text ?? "");
        }

        private void SetState(CaptureState state)
        {
            if (State == state) return;
            State = state;
            if (StateChanged != null) StateChanged(state);
        }

        private void NotifySnapshot()
        {
            if (SnapshotChanged != null) SnapshotChanged(Snapshot());
        }

        private static string SafeFileName(string value)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') builder.Append(c);
                else builder.Append('_');
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            if (_active)
            {
                StopAndExport("disposed while active");
            }
            else
            {
                if (_rawStream != null) { _rawStream.Dispose(); _rawStream = null; }
                if (_transcript != null) { _transcript.Dispose(); _transcript = null; }
            }
        }
    }
}
