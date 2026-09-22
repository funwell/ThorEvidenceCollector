# ThorEvidenceCollector Design

## 1. Objective

Deliver a portable Windows collector that a group member can use with minimal effort:

1. Double-click one executable.
2. Click `开始采集` once.
3. Power-cycle the authorized Thor/ADCU bench unit when the UI says `等待设备上电`.
4. Wait for automatic completion. The tool creates one ZIP that can be sent back without copying console output manually.

The collector must capture evidence for `mcu_version`, `help`, `showvoltages`, `pmstateget`, `readtemp`, the complete boot transcript, and power-state transitions. It must never attempt firmware changes or non-read-only power-control commands.

## 2. Scope and Safety

- Windows 10/11 x64, no administrator privilege, no network access, no installer.
- Target framework: .NET Framework 4.8-compatible WinForms; compile with the inbox `csc.exe` so the distributable is a small single EXE.
- Serial settings: 115200, 8 data bits, no parity, 1 stop bit, no hardware/software flow control. The UI may offer 19200 and 921600 as explicit manual alternatives, but default and automatic mode must use 115200.
- The application has no free-form command input.
- Hard-coded allowed commands only: `version`, `help`, `showvoltages`, `pmstateget`, `readtemp`, `pmrunstate`, `pncstatus`, `socstatus`, `readvolt`, `readvrs12`, `swtlinkstatus`, `swtSqiValue`, `swtCrcCount`, `swtstatusdata`.
- Explicitly reject and never transmit: `poweron`, `poweronIST`, `tegrapoweron`, `poweroff`, `poweroffIST`, `tegrareset`, `tegrarecovery`, `aurixreset`, `zkrmcu`, `zkrcfg`, `cycliccanon`, `pnc29`, `voltageMonitor disable`, all unknown commands, and command strings containing separators or arguments.
- Do not automatically toggle DTR/RTS, send break, reset the port, or retry by opening every COM port indiscriminately.
- If the port is unavailable or locked, show a diagnostic and stop safely. Do not fall back to transmitting on another arbitrary port without user-visible selection.
- Preserve raw received bytes exactly. Decode a separate human transcript with replacement characters for invalid bytes.
- Do not collect Windows username, hostname, IP address, browser data, credentials, or unrelated files. Collect only selected COM name, sanitized USB caption, baud, timestamps, and serial evidence.

## 3. Serial Discovery

Use WMI/System.Management (`Win32_PnPEntity` and `Win32_SerialPort`) to enumerate COM ports and captions. The selected B channel is the active MCU/NvShell port; a distinct A channel is opened separately as an RX-only passive port. Prefer, in order:

1. A port whose caption contains `USB-Enhanced-SERIAL-B` or `CH342` and `B`.
2. A port whose caption contains `USB-Enhanced-SERIAL-B`.
3. A user-selected port from the visible dropdown.

Show every discovered port as `COMx - sanitized caption`; never hide ports. If WMI is unavailable, fall back to `SerialPort.GetPortNames()` and explain that automatic B-channel identification is unavailable.

Open the active B port and, when available, one distinct A port. Set `DtrEnable=false`, `RtsEnable=false`, `Handshake=None`, and a short read timeout on both. The A port must never receive CRLF, commands, break, or any write. Do not close an existing user process or kill another monitor.

## 4. Capture State Machine

States:

- `Idle`
- `OpeningPort`
- `RecordingBeforePower`
- `WaitingForNvShell`
- `WaitingForPrompt`
- `CollectingReadOnlyCommands`
- `WatchingPowerTransition`
- `Completed`
- `Stopped`
- `Faulted`

### Start

- Create a unique session directory under `Evidence\\yyyyMMdd-HHmmss`.
- Write `session.json` metadata immediately.
- Open the selected port and begin writing `raw-rx.bin` before sending anything.
- Start `serial-transcript.log` with local and UTC timestamps.
- UI must say: `正在录制。现在可以给设备上电；程序不会发送上电/复位命令。`

### Banner and prompt handling

- Parse data across arbitrary byte-chunk boundaries.
- Detect `NvShell Initialization Start`, `NvShell Initialized`, `Press 'Enter'`, and `NvShell>` case-insensitively.
- On a fresh initialization banner, record a new boot-cycle object; do not erase earlier cycles.
- Send one CRLF only after the banner/prompt hint is observed. Record it in the transcript as `TX [safe CRLF]`.
- If no prompt is observed, do not send the read-only queue. Continue passive capture and mark `prompt=false`.
- If the device was already powered before capture, mark `bootBanner=false` but still collect commands if a prompt is visible.

### Read-only queue

- Once a prompt is confirmed, send the allowlisted commands one at a time, with CRLF.
- Minimum inter-command delay: 1200 ms. Per-command response timeout: 6000 ms.
- Each command is sent at most once per boot cycle. A timeout is recorded; the queue continues.
- Detect the next prompt or timeout to delimit each command output. Save separate files under `commands\\<ordinal>-<command>.txt`.
- `help` is expected to be large; do not truncate the raw or command file. UI may cap visible text only.

### Completion

Complete automatically when either condition holds:

- All read-only commands have been attempted and a power-down/sleep transition is observed, followed by 3000 ms quiet time.
- 90 seconds have elapsed since the first boot banner or first received byte after start.

Allow manual `停止并导出` at any time. On disconnect, exception, or form close, flush and export the partial session with an explicit `partial=true` reason. Never discard partial evidence.

## 5. Evidence Parser

Maintain both the complete event stream and the latest normalized snapshot. Parse fragmented and repeated lines.

Required normalized fields:

- `mcuVersion`, `mcuA`, `buildDate`, `platformCommitId`
- `defaultBootChain`, `nextBootChain`, `bomId`, `boardBomId`
- `nvShellSeen`, `promptSeen`, `bootBannerSeen`, `bootCycleCount`
- `kl30Vbat`, `vdd12`, `vbatSoc`, `acc1`, `vrs5`, `prereg16`, `prereg5`
- `serdes18`, `serdes12`, `serdes10`, `socUfs`, `hsPower2`
- `pgPwrSerdes`, `pgSocVrs11`, `socPgLcvr`, `socPgLcvrAo`
- `mcuTemp`, `socTemp`, `coolantTemp`
- `wakeSource`, `powerDeviceStatus`, `pmState`, ordered `pmTransitions`
- `powerUpRequested`, `powerDownRequested`, `powerDownReasonLines`
- `dtcSocLink`, `dtcDhuLink`, `socTempReadFailed`
- `commandsAttempted`, `commandsTimedOut`, `warnings`

Warnings should be generated, not asserted as root-cause claims. Examples:

- `ACC1 is 0 V in at least one sample`
- `SoC/SerDes/UFS rails remain near zero`
- `MCU requested powerdown`
- `wake source is zero`
- `boot banner not captured`
- `prompt not captured`
- `command timed out`
- `captured build differs from known sample`

## 6. Export Format

Every session must contain:

- `raw-rx.bin`: exact received bytes.
- `serial-A-rx.bin` and `serial-A-transcript.log`: optional passive A-channel evidence; no TX is ever sent to this port.
- `serial-transcript.log`: timestamped RX/TX transcript; TX lines identify only safe CRLF and allowlisted commands.
- `summary.json`: schema version, timestamps, port/baud, capture flags, normalized fields, events, warnings, command status, and file hashes.
- `summary.txt`: concise Chinese/English-readable report for a forum post.
- `commands\\*.txt`: raw output for each read-only command.
- `README.txt`: how the capture was made and the explicit safety policy.
- `manifest.sha256`: SHA-256 for every evidence file.

Create `ThorEvidence-yyyyMMdd-HHmmss.zip` automatically beside the session directory. The UI must show the absolute ZIP path and provide `复制摘要` and `打开导出目录` buttons.

JSON must be generated with a real serializer or a rigorously escaped helper; never concatenate unescaped user/device text into JSON.

## 7. User Interface

The first screen must make the zero-cost flow obvious:

- Port dropdown with caption and recommended B-channel marker.
- Baud dropdown, default 115200.
- Large safety banner: `本工具只监听并发送 5 条只读命令，不会 poweron/复位/刷写。`
- Buttons: `开始采集`, `停止并导出`, `复制摘要`, `打开导出目录`.
- Status cards: `串口`, `NvShell`, `录制`, `当前阶段`, `剩余时间`.
- Live transcript with auto-scroll, pause-scroll option, and a visible `TX`/`RX` distinction.
- Event timeline: banner, prompt, each command, timeout, power transition, export.
- On port exceptions show a human explanation and preserve any partial data.

No command textbox is required for the group distribution build.

## 8. Replay and Tests

Build a testable core independent of WinForms and physical serial hardware.

- `CollectorCoreTests.exe` must test command allow/deny policy, fragmented input, invalid bytes, repeated boot cycles, prompt detection, voltage/state parsing, timeout bookkeeping, summary JSON escaping, and SHA-256 manifest generation.
- `--replay <file>` must parse an existing `.log`/`.txt` without opening a serial port and export the same summary schema.
- `--demo` must open the GUI and feed a synthetic boot/powerdown stream, allowing visual QA without hardware.
- Test a missing port, port already in use, empty input, binary bytes, no prompt, unsupported command output, partial stop, path with spaces/non-ASCII, and export failure.
- A test must assert that the transcript contains none of the forbidden command strings as transmitted commands.

## 9. Build and Delivery

Required scripts:

- `build.ps1`: compile the WinForms EXE and test executable using inbox .NET Framework `csc.exe`.
- `test.ps1`: compile/run tests and return nonzero on failure.
- `package.ps1`: run tests, build, copy README, and create a versioned ZIP under `dist`.

The final verification must:

1. Run the self-tests and replay fixture.
2. Start the actual EXE in `--demo` mode and verify the window title and export output.
3. Stop/clean demo processes.
4. Report exact EXE and ZIP paths.

Do not claim hardware capture was tested unless a real serial session was performed. Label demo/replay evidence as synthetic.
