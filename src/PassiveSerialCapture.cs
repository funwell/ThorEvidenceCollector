using System;
using System.IO;
using System.Text;

namespace ThorEvidence.Core
{
    public sealed class PassiveSerialCapture : IDisposable
    {
        private readonly UTF8Encoding _utf8 = new UTF8Encoding(false);
        private readonly EvidenceParser _parser = new EvidenceParser();
        private FileStream _rawStream;
        private StreamWriter _transcript;

        public string PortName { get; private set; }
        public string Caption { get; private set; }
        public int BaudRate { get; private set; }
        public string SessionDirectory { get; private set; }
        public int RawBytes { get; private set; }
        public bool IsActive { get { return _rawStream != null; } }

        public void Start(string sessionDirectory, string portName, string caption, int baudRate)
        {
            if (IsActive) throw new InvalidOperationException("Passive capture is already active.");
            SessionDirectory = Path.GetFullPath(sessionDirectory);
            Directory.CreateDirectory(SessionDirectory);
            PortName = portName ?? "";
            Caption = caption ?? "";
            BaudRate = baudRate;
            RawBytes = 0;
            _rawStream = new FileStream(Path.Combine(SessionDirectory, "serial-A-rx.bin"), FileMode.Create, FileAccess.Write, FileShare.Read, 65536, FileOptions.WriteThrough);
            _transcript = new StreamWriter(Path.Combine(SessionDirectory, "serial-A-transcript.log"), false, _utf8);
            _transcript.AutoFlush = true;
            WriteTranscript("INFO", "PASSIVE SESSION START port=" + PortName + " baud=" + BaudRate + " 8N1 flow=none");
        }

        public void ProcessReceived(byte[] bytes)
        {
            if (!IsActive || bytes == null || bytes.Length == 0) return;
            _rawStream.Write(bytes, 0, bytes.Length);
            _rawStream.Flush();
            RawBytes += bytes.Length;
            string text = _utf8.GetString(bytes);
            _parser.Append(text);
            WriteTranscript("RX", text);
        }

        public void Stop()
        {
            if (!IsActive) return;
            try
            {
                WriteTranscript("INFO", "PASSIVE SESSION END");
                if (_rawStream != null) { _rawStream.Flush(); _rawStream.Dispose(); _rawStream = null; }
                if (_transcript != null) { _transcript.Flush(); _transcript.Dispose(); _transcript = null; }
                EvidenceSnapshot snapshot = _parser.Snapshot();
                File.WriteAllText(
                    Path.Combine(SessionDirectory, "serial-A-summary.json"),
                    EvidenceFormatter.BuildJson(snapshot, PortName, Caption, false, new string[0]),
                    _utf8);
                File.WriteAllText(
                    Path.Combine(SessionDirectory, "serial-A-summary.txt"),
                    EvidenceFormatter.BuildSummary(snapshot, PortName, Caption, false, new string[0]),
                    _utf8);
            }
            finally
            {
                if (_rawStream != null) { _rawStream.Dispose(); _rawStream = null; }
                if (_transcript != null) { _transcript.Dispose(); _transcript = null; }
            }
        }

        private void WriteTranscript(string kind, string text)
        {
            if (_transcript == null) return;
            string safe = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = safe.Split(new char[] { '\n' });
            for (int i = 0; i < lines.Length; i++)
            {
                _transcript.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + kind + "] " + lines[i]);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
