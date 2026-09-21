# ThorEvidenceCollector

一个面向 NVIDIA DRIVE Thor-U / 汽车 ADCU `NvShell` 的 Windows 串口证据采集器。

它用于在授权的台架设备上采集启动过程、MCU 电源状态、电压和唤醒线索，方便多人协作分析。项目只提交源码和测试，不提交 EXE、真实串口日志或用户设备数据。

## 设计目标

- 双击即可使用的 WinForms 采集器，目标是 Windows 10/11。
- 自动识别 CH342 双串口并优先标记 `USB-Enhanced-SERIAL-B`。
- 先录制原始 RX，再等待设备上电。
- 自动采集完整启动日志，并保存原始字节流。
- 只发送固定的只读状态命令，不提供自由命令输入。
- 自动生成 `raw-rx.bin`、串口 transcript、结构化 JSON、摘要、命令输出和 SHA-256 清单。
- 支持 `--replay` 和 `--demo`，没有真实设备也能测试解析器和导出流程。

## 安全边界

默认只允许发送这些无参数只读命令：

```text
version
help
showvoltages
pmstateget
readtemp
pmrunstate
pncstatus
socstatus
readvolt
readvrs12
swtlinkstatus
swtSqiValue
swtCrcCount
swtstatusdata
```

采集器不会发送 `poweron`、`poweroff`、`poweronIST`、`tegrapoweron`、`tegrareset`、`tegrarecovery`、`aurixreset`、`zkrmcu`、刷写命令、CAN 报文或任意未知命令。DTR/RTS 关闭，不发送 break，不关闭其他串口程序。

真实设备采集前，应确认散热正常；采集器不会替用户给设备上电，也不会修改设备固件。

## 直接使用已构建包

发布包应包含：

```text
ThorEvidenceCollector.exe
Start-ThorEvidenceCollector.cmd
README.txt
```

操作步骤：

1. 连接 C2C 调试线，等待 Windows 识别 CH342 串口。
2. 双击 `ThorEvidenceCollector.exe`，选择带有 `USB-Enhanced-SERIAL-B` 或 `[推荐 B 通道]` 的端口。
3. 点击“开始采集”，再给设备上电；完成后把生成的 `ThorEvidence-*.zip` 发给分析人员。

如果现有的串口监控程序占用同一个 COM 口，先关闭它。`.cmd` 只是备用启动器，不需要和 EXE 同时运行。

## 从源码构建

项目使用 Windows 自带的 .NET Framework `csc.exe`，不依赖 Python、Node、NuGet 或管理员权限。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -Version 20260921
```

产物位于 `dist/`，但 `dist/` 已被 `.gitignore` 排除，不应提交到仓库。

无硬件测试：

```powershell
.\dist\ThorEvidenceCollector.exe --self-test
.\dist\ThorEvidenceCollector.exe --replay .\fixtures\synthetic-boot.log
.\dist\ThorEvidenceCollector.exe --demo
```

`--demo` 生成的是合成证据，不能冒充真实车机采集结果。

## macOS

仓库同时提供 Python 版 macOS/Linux CLI 采集器，不需要运行 Windows EXE。参见 [`mac/README.md`](mac/README.md)。首次准备：

```bash
python3 -m venv .venv
. .venv/bin/activate
python -m pip install -r mac/requirements.txt
python mac/thor_nvshell_collect.py --port /dev/cu.usbserial-XXXX
```

## 导出结构

每次采集会生成一个独立目录和 ZIP，主要包含：

```text
raw-rx.bin
serial-transcript.log
summary.json
summary.txt
commands/*.txt
README.txt
manifest.sha256
```

其中 `raw-rx.bin` 保存收到的原始字节；`summary.json` 包含 MCU 版本、电压、SoC rails、PM 状态、wake source、DTC/错误线索和采集警告。

## 给其他 AI Agent 的维护提示词

下面的提示词不依赖本项目的专用工具，适用于任何能访问本地源码和 Windows 串口的编码/分析 Agent：

```text
你是 ThorEvidenceCollector 的维护与取证 Agent。先阅读 README.md、DESIGN.md、AGENTS.md 和现有测试，再修改代码。

目标：维护一个 Windows 10/11、.NET Framework 4.x 的 Thor NvShell 串口证据采集器。它必须在用户授权的台架设备上被动记录启动过程，并自动保存可复核的原始字节、解码 transcript、结构化 JSON、命令输出、摘要和 SHA-256 清单。

硬性安全规则：
- 只允许发送无参数只读命令：version、help、showvoltages、pmstateget、readtemp、pmrunstate、pncstatus、socstatus、readvolt、readvrs12、swtlinkstatus、swtSqiValue、swtCrcCount、swtstatusdata。
- 严禁发送 poweron、poweroff、poweronIST、tegrapoweron、tegrareset、tegrarecovery、aurixreset、zkrmcu、任何刷写命令、CAN 报文、GPIO 写入或未知命令。
- 不要自动切换 DTR/RTS，不要发送 break，不要关闭或杀死其他串口程序。
- 不要收集用户名、密码、网络凭据、浏览器数据或与设备无关的文件。
- 真实硬件未连接时，只使用 replay/demo/合成夹具验证；合成结果必须明确标记，不能作为真实硬件证据。

串口要求：优先发现 CH342 的 USB-Enhanced-SERIAL-B，默认 115200 8N1、无流控；开始采集后先录制原始 RX，再等待设备上电。必须支持任意字节分片、重复 NvShell 启动、无 prompt、串口占用、设备中途断电、超时、乱码和正在写入的 replay 文件。

开发流程：
1. 先写一个能失败的测试，再实现最小修改；不要以“看起来能工作”代替测试。
2. 每次变更运行 test.ps1；构建运行 build.ps1；交付前运行 package.ps1。
3. 必须检查 ZIP 内容、SHA-256、--self-test、--replay、--demo，并确认没有残留进程。
4. 汇报时列出改动文件、测试命令、退出码、真实/合成证据的区别和未验证项。

分析真实日志时，优先区分 MCU 电源状态机、SoC/SerDes/UFS rails、wake source、KL30/ACC1 和 Linux 是否真正启动；不要仅凭一条 ACC1 或一条 DTC 断言根因。
```

## 许可证

MIT License，见 [`LICENSE`](LICENSE)。
