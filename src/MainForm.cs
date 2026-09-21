using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using ThorEvidence.Core;

namespace ThorEvidenceCollector
{
    public sealed class MainForm : Form
    {
        private readonly bool _demo;
        private readonly string _outputRoot;
        private ComboBox _portCombo;
        private ComboBox _baudCombo;
        private Button _refreshButton;
        private Button _startButton;
        private Button _stopButton;
        private Button _copyButton;
        private Button _openButton;
        private CheckBox _pauseScroll;
        private Label _diagnosticLabel;
        private Label _serialLabel;
        private Label _shellLabel;
        private Label _recordLabel;
        private Label _stageLabel;
        private Label _remainingLabel;
        private Label _snapshotLabel;
        private Label _warningLabel;
        private RichTextBox _transcriptBox;
        private ListBox _timeline;
        private Timer _timer;
        private SerialPort _serial;
        private DemoTransport _demoTransport;
        private CaptureSession _session;
        private DateTime _demoCloseDueUtc;
        private string _lastZipPath = "";
        private PortDiscoveryResult _ports;

        public MainForm(bool demo, string outputRoot)
        {
            _demo = demo;
            _outputRoot = String.IsNullOrEmpty(outputRoot) ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(outputRoot);
            InitializeUi();
            Shown += OnShown;
        }

        private void InitializeUi()
        {
            Text = _demo ? "ThorEvidenceCollector - DEMO" : "ThorEvidenceCollector - Thor NvShell 证据采集器";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 820);
            MinimumSize = new Size(980, 640);
            BackColor = Color.FromArgb(245, 247, 250);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 142;
            Controls.Add(top);

            Label safety = new Label();
            safety.Text = "安全模式：本工具只监听并发送 5 条只读命令，不会 poweron/复位/刷写/CAN。";
            safety.BackColor = Color.FromArgb(104, 35, 35);
            safety.ForeColor = Color.White;
            safety.Font = new Font("Microsoft YaHei", 11, FontStyle.Bold);
            safety.TextAlign = ContentAlignment.MiddleLeft;
            safety.Padding = new Padding(12, 0, 12, 0);
            safety.Location = new Point(10, 8);
            safety.Size = new Size(1200, 38);
            safety.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(safety);

            Label portLabel = new Label();
            portLabel.Text = "串口";
            portLabel.Location = new Point(14, 61);
            portLabel.AutoSize = true;
            top.Controls.Add(portLabel);

            _portCombo = new ComboBox();
            _portCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _portCombo.Location = new Point(52, 57);
            _portCombo.Width = 330;
            top.Controls.Add(_portCombo);

            _refreshButton = new Button();
            _refreshButton.Text = "刷新";
            _refreshButton.Location = new Point(388, 56);
            _refreshButton.Width = 58;
            _refreshButton.Click += delegate { RefreshPorts(); };
            top.Controls.Add(_refreshButton);

            Label baudLabel = new Label();
            baudLabel.Text = "波特率";
            baudLabel.Location = new Point(460, 61);
            baudLabel.AutoSize = true;
            top.Controls.Add(baudLabel);

            _baudCombo = new ComboBox();
            _baudCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _baudCombo.Items.AddRange(new object[] { 115200, 19200, 921600 });
            _baudCombo.SelectedIndex = 0;
            _baudCombo.Location = new Point(512, 57);
            _baudCombo.Width = 92;
            top.Controls.Add(_baudCombo);

            _startButton = new Button();
            _startButton.Text = "开始采集";
            _startButton.Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            _startButton.Location = new Point(620, 55);
            _startButton.Width = 104;
            _startButton.Click += delegate { StartCapture(); };
            top.Controls.Add(_startButton);

            _stopButton = new Button();
            _stopButton.Text = "停止并导出";
            _stopButton.Location = new Point(734, 55);
            _stopButton.Width = 104;
            _stopButton.Enabled = false;
            _stopButton.Click += delegate { StopCapture("manual stop"); };
            top.Controls.Add(_stopButton);

            _copyButton = new Button();
            _copyButton.Text = "复制摘要";
            _copyButton.Location = new Point(848, 55);
            _copyButton.Width = 90;
            _copyButton.Click += delegate { CopySummary(); };
            top.Controls.Add(_copyButton);

            _openButton = new Button();
            _openButton.Text = "打开导出目录";
            _openButton.Location = new Point(948, 55);
            _openButton.Width = 118;
            _openButton.Click += delegate { OpenExportDirectory(); };
            top.Controls.Add(_openButton);

            _pauseScroll = new CheckBox();
            _pauseScroll.Text = "暂停自动滚动";
            _pauseScroll.AutoSize = true;
            _pauseScroll.Location = new Point(1080, 60);
            top.Controls.Add(_pauseScroll);

            _diagnosticLabel = new Label();
            _diagnosticLabel.Text = "正在枚举串口...";
            _diagnosticLabel.ForeColor = Color.FromArgb(85, 85, 85);
            _diagnosticLabel.Location = new Point(14, 94);
            _diagnosticLabel.AutoEllipsis = true;
            _diagnosticLabel.Size = new Size(1180, 22);
            top.Controls.Add(_diagnosticLabel);

            Label hint = new Label();
            hint.Text = _demo ? "DEMO：使用合成分片数据，不连接真实串口；导出后自动关闭。" : "开始后先录制原始 RX，再等待设备自行上电；不会自动发送 poweron。";
            hint.ForeColor = _demo ? Color.DarkBlue : Color.DarkGreen;
            hint.Location = new Point(14, 116);
            hint.AutoSize = true;
            top.Controls.Add(hint);

            Panel status = new Panel();
            status.Dock = DockStyle.Top;
            status.Height = 96;
            status.Padding = new Padding(10, 8, 10, 4);
            Controls.Add(status);

            _serialLabel = NewStatusLabel("串口: 未开始", 0, 5, 280);
            status.Controls.Add(_serialLabel);
            _shellLabel = NewStatusLabel("NvShell: 未识别", 290, 5, 280);
            status.Controls.Add(_shellLabel);
            _recordLabel = NewStatusLabel("录制: 未开始", 580, 5, 220);
            status.Controls.Add(_recordLabel);
            _stageLabel = NewStatusLabel("阶段: Idle", 810, 5, 300);
            status.Controls.Add(_stageLabel);
            _remainingLabel = NewStatusLabel("剩余时间: -", 0, 40, 280);
            status.Controls.Add(_remainingLabel);
            _snapshotLabel = NewStatusLabel("快照: 等待数据", 290, 40, 520);
            status.Controls.Add(_snapshotLabel);
            _warningLabel = NewStatusLabel("异常: 无", 810, 40, 300);
            _warningLabel.ForeColor = Color.DarkRed;
            status.Controls.Add(_warningLabel);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterDistance = 850;
            split.Panel1.Padding = new Padding(10, 0, 5, 10);
            split.Panel2.Padding = new Padding(5, 0, 10, 10);
            Controls.Add(split);

            _transcriptBox = new RichTextBox();
            _transcriptBox.Dock = DockStyle.Fill;
            _transcriptBox.ReadOnly = true;
            _transcriptBox.BackColor = Color.FromArgb(24, 28, 34);
            _transcriptBox.ForeColor = Color.Gainsboro;
            _transcriptBox.Font = new Font("Consolas", 9);
            _transcriptBox.WordWrap = false;
            split.Panel1.Controls.Add(_transcriptBox);

            Label timelineLabel = new Label();
            timelineLabel.Text = "事件时间线";
            timelineLabel.Dock = DockStyle.Top;
            timelineLabel.Height = 25;
            split.Panel2.Controls.Add(timelineLabel);

            _timeline = new ListBox();
            _timeline.Dock = DockStyle.Fill;
            _timeline.Font = new Font("Microsoft YaHei", 9);
            split.Panel2.Controls.Add(_timeline);

            _timer = new Timer();
            _timer.Interval = 50;
            _timer.Tick += OnTimerTick;
            FormClosing += OnFormClosing;
        }

        private Label NewStatusLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 27);
            label.BorderStyle = BorderStyle.FixedSingle;
            label.BackColor = Color.White;
            label.Padding = new Padding(6, 4, 4, 2);
            label.AutoEllipsis = true;
            return label;
        }

        private void OnShown(object sender, EventArgs e)
        {
            RefreshPorts();
            if (_demo)
            {
                BeginInvoke((MethodInvoker)delegate { StartCapture(); });
            }
        }

        private void RefreshPorts()
        {
            if (_demo) return;
            _ports = PortDiscovery.Enumerate();
            _portCombo.Items.Clear();
            for (int i = 0; i < _ports.Ports.Count; i++) _portCombo.Items.Add(_ports.Ports[i]);
            if (_ports.Ports.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < _ports.Ports.Count; i++) if (_ports.Ports[i].RecommendationScore > _ports.Ports[best].RecommendationScore) best = i;
                _portCombo.SelectedIndex = best;
            }
            string diagnostic = _ports.Diagnostic;
            if (_ports.Ports.Count == 0) diagnostic = "未发现串口。请连接 CH342 后点击刷新；不会自动尝试未知端口。" + (String.IsNullOrEmpty(diagnostic) ? "" : " " + diagnostic);
            else if (!String.IsNullOrEmpty(diagnostic)) diagnostic = "已发现串口，但自动识别提示：" + diagnostic;
            else diagnostic = "已发现 " + _ports.Ports.Count.ToString() + " 个串口；优先选择 CH342 B 通道。";
            _diagnosticLabel.Text = diagnostic;
        }

        private void StartCapture()
        {
            if (_session != null && _session.IsActive) return;
            PortInfo selected = _demo ? null : (_portCombo.SelectedItem as PortInfo);
            if (!_demo && selected == null)
            {
                MessageBox.Show(this, "没有可用的已选择串口。请连接设备后刷新，并明确选择一个端口。", "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (_demo)
                {
                    _demoTransport = new DemoTransport();
                    _session = new CaptureSession(delegate(byte[] bytes) { return _demoTransport.Write(bytes); }, true);
                }
                else
                {
                    _session = new CaptureSession(SendToSerial, false);
                }
                SubscribeSession(_session);
                string evidenceRoot = Path.Combine(_outputRoot, "Evidence");
                string sessionDirectory = SessionPaths.CreateSessionDirectory(evidenceRoot, _demo ? "Demo" : "Capture");
                int baud = Convert.ToInt32(_baudCombo.SelectedItem);
                _session.Start(sessionDirectory, _demo ? "DEMO" : selected.PortName, _demo ? "synthetic demo" : selected.Caption, baud);

                if (_demo)
                {
                    _demoTransport.EnqueueInitial();
                }
                else
                {
                    OpenSerial(selected.PortName, baud);
                }

                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _refreshButton.Enabled = false;
                _portCombo.Enabled = false;
                _baudCombo.Enabled = false;
                _timer.Start();
                AddTimeline("开始采集：录制已先于任何发送启动");
            }
            catch (Exception ex)
            {
                ShowError("启动采集失败：" + ex.Message);
                if (_session != null) _session.FailAndExport("start failed: " + ex.Message);
                CloseSerial();
            }
        }

        private void OpenSerial(string portName, int baud)
        {
            _serial = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            _serial.Handshake = Handshake.None;
            _serial.DtrEnable = false;
            _serial.RtsEnable = false;
            _serial.ReadTimeout = 50;
            _serial.WriteTimeout = 1000;
            _serial.ReadBufferSize = 65536;
            _serial.Open();
            _session.AddEvent("OPEN " + portName + " @ " + baud + " 8N1, DTR=OFF, RTS=OFF, flow-control=none");
            _diagnosticLabel.Text = "串口已打开。程序正在被动等待设备上电，不会发送 poweron。";
        }

        private bool SendToSerial(byte[] bytes)
        {
            if (_serial == null || !_serial.IsOpen) return false;
            _serial.Write(bytes, 0, bytes.Length);
            return true;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (_session == null) return;
                if (_demo)
                {
                    int count = 0;
                    while (_demoTransport.HasData && count < 24)
                    {
                        byte[] chunk = _demoTransport.ReadChunk();
                        if (chunk == null) break;
                        _session.ProcessReceived(chunk);
                        count++;
                    }
                }
                else if (_serial != null && _serial.IsOpen)
                {
                    while (_serial.BytesToRead > 0)
                    {
                        int count = Math.Min(_serial.BytesToRead, 8192);
                        byte[] bytes = new byte[count];
                        int read = _serial.Read(bytes, 0, count);
                        if (read <= 0) break;
                        if (read != bytes.Length)
                        {
                            byte[] exact = new byte[read];
                            Buffer.BlockCopy(bytes, 0, exact, 0, read);
                            bytes = exact;
                        }
                        _session.ProcessReceived(bytes);
                    }
                }
                _session.Tick();
                UpdateStatus(_session.Snapshot());
                if (!_session.IsActive && _session.IsExported)
                {
                    CloseSerial();
                    _stopButton.Enabled = false;
                    _startButton.Enabled = true;
                    _refreshButton.Enabled = true;
                    _portCombo.Enabled = true;
                    _baudCombo.Enabled = true;
                    if (_demo && _demoCloseDueUtc == DateTime.MinValue) _demoCloseDueUtc = DateTime.UtcNow.AddMilliseconds(1200);
                    if (!_demo) _timer.Stop();
                }
                if (_demo && _demoCloseDueUtc != DateTime.MinValue && DateTime.UtcNow >= _demoCloseDueUtc)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                AppendLive("ERROR", ex.Message);
                if (_session != null && _session.IsActive) _session.FailAndExport("runtime serial error: " + ex.Message);
                CloseSerial();
            }
        }

        private void StopCapture(string reason)
        {
            if (_session == null) return;
            try
            {
                _session.StopAndExport(reason);
            }
            catch (Exception ex)
            {
                ShowError("导出失败：" + ex.Message);
            }
            _timer.Stop();
            CloseSerial();
            _stopButton.Enabled = false;
            _startButton.Enabled = true;
            _refreshButton.Enabled = true;
            _portCombo.Enabled = true;
            _baudCombo.Enabled = true;
            UpdateStatus(_session.Snapshot());
        }

        private void CloseSerial()
        {
            if (_serial == null) return;
            try { if (_serial.IsOpen) _serial.Close(); } catch { }
            try { _serial.Dispose(); } catch { }
            _serial = null;
        }

        private void SubscribeSession(CaptureSession session)
        {
            session.Log += OnSessionLog;
            session.StateChanged += delegate(CaptureState state) { UpdateStage(state); };
            session.SnapshotChanged += UpdateStatus;
            session.Exported += delegate(string path)
            {
                _lastZipPath = path;
                AddTimeline("导出完成: " + path);
                _diagnosticLabel.Text = "已导出 ZIP：" + path;
            };
        }

        private void OnSessionLog(string kind, string text)
        {
            AppendLive(kind, text);
            if (kind == "EVENT" || kind == "WARN" || kind == "EXPORT" || kind == "INFO") AddTimeline("[" + kind + "] " + OneLine(text));
        }

        private void AppendLive(string kind, string text)
        {
            if (_transcriptBox == null) return;
            string value = text ?? "";
            if (value.Length > 120000) value = value.Substring(0, 120000) + "\r\n[UI: visible text capped; evidence files are not truncated]";
            value = value.Replace("\0", "�");
            string[] lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split(new char[] { '\n' });
            Color color = kind == "TX" ? Color.LightSkyBlue : (kind == "WARN" || kind == "ERROR" ? Color.Salmon : (kind == "EVENT" || kind == "EXPORT" ? Color.Khaki : Color.Gainsboro));
            for (int i = 0; i < lines.Length; i++)
            {
                _transcriptBox.SelectionStart = _transcriptBox.TextLength;
                _transcriptBox.SelectionLength = 0;
                _transcriptBox.SelectionColor = color;
                _transcriptBox.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + kind + "] " + lines[i] + Environment.NewLine);
            }
            if (_transcriptBox.TextLength > 300000)
            {
                _transcriptBox.Select(0, 60000);
                _transcriptBox.SelectedText = "[UI: older visible text removed; raw-rx.bin and serial-transcript.log are complete]" + Environment.NewLine;
            }
            if (!_pauseScroll.Checked)
            {
                _transcriptBox.SelectionStart = _transcriptBox.TextLength;
                _transcriptBox.ScrollToCaret();
            }
        }

        private void UpdateStage(CaptureState state)
        {
            if (_stageLabel != null) _stageLabel.Text = "阶段: " + StateText(state);
        }

        private void UpdateStatus(EvidenceSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (_session == null)
            {
                _serialLabel.Text = "串口: 未开始";
                return;
            }
            _serialLabel.Text = "串口: " + _session.PortName + " @ " + _session.BaudRate;
            _shellLabel.Text = "NvShell: " + (snapshot.PromptSeen ? "提示符已发现" : (snapshot.NvShellSeen ? "已发现" : "未识别"));
            _recordLabel.Text = "录制: " + (_session.IsActive ? "进行中" : (_session.IsExported ? "已导出" : "已停止")) + " / " + _session.RawBytes + " bytes";
            UpdateStage(_session.State);
            if (_session.IsActive)
            {
                double seconds = (DateTime.UtcNow - _session.StartedUtc).TotalSeconds;
                _remainingLabel.Text = "剩余时间: " + Math.Max(0, 90 - (int)seconds).ToString() + " s window";
            }
            else _remainingLabel.Text = "剩余时间: -";
            _snapshotLabel.Text = "MCU: " + snapshot.McuVersion + " | PM: " + snapshot.PmState + " | ACC1: " + snapshot.Acc1 + " V | SoC UFS: " + snapshot.SocUfs + " V";
            _warningLabel.Text = snapshot.Warnings.Count == 0 ? "异常: 无" : "异常: " + snapshot.Warnings[snapshot.Warnings.Count - 1];
        }

        private void AddTimeline(string text)
        {
            if (_timeline == null) return;
            _timeline.Items.Add(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text);
            _timeline.TopIndex = Math.Max(0, _timeline.Items.Count - 1);
        }

        private static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        private void CopySummary()
        {
            try
            {
                string text = "";
                if (_session != null && !String.IsNullOrEmpty(_session.SessionDirectory))
                {
                    string path = Path.Combine(_session.SessionDirectory, "summary.txt");
                    if (File.Exists(path)) text = File.ReadAllText(path, Encoding.UTF8);
                    else text = EvidenceFormatter.BuildSummary(_session.Snapshot(), _session.PortName, _session.PortCaption, _session.Synthetic, CommandPolicy.AllowedCommands);
                }
                if (String.IsNullOrEmpty(text)) text = "尚未开始采集。";
                Clipboard.SetText(text);
                _diagnosticLabel.Text = "摘要已复制到剪贴板。";
            }
            catch (Exception ex)
            {
                ShowError("复制摘要失败：" + ex.Message);
            }
        }

        private void OpenExportDirectory()
        {
            string path = _session == null ? Path.Combine(_outputRoot, "Evidence") : _session.SessionDirectory;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + path + "\"", UseShellExecute = true });
            }
            catch (Exception ex) { ShowError("打开目录失败：" + ex.Message); }
        }

        private void ShowError(string message)
        {
            _diagnosticLabel.Text = message;
            AppendLive("ERROR", message);
            AddTimeline(message);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            if (_session != null && _session.IsActive)
            {
                try { _session.StopAndExport("window closed"); } catch { }
            }
            CloseSerial();
        }

        private static string StateText(CaptureState state)
        {
            switch (state)
            {
                case CaptureState.OpeningPort: return "OpeningPort";
                case CaptureState.RecordingBeforePower: return "正在录制/等待上电";
                case CaptureState.WaitingForNvShell: return "等待 NvShell";
                case CaptureState.WaitingForPrompt: return "等待提示符";
                case CaptureState.CollectingReadOnlyCommands: return "采集只读命令";
                case CaptureState.WatchingPowerTransition: return "观察电源状态";
                case CaptureState.Completed: return "已完成";
                case CaptureState.Stopped: return "已停止";
                case CaptureState.Faulted: return "异常导出";
                default: return "Idle";
            }
        }
    }
}
