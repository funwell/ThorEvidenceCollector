# macOS 使用说明

Windows 的 `ThorEvidenceCollector.exe` 不能直接在 macOS 原生运行，因此仓库提供一个 Python 版 CLI 采集器。它使用同一套只读命令白名单和导出字段，不需要 Windows 虚拟机。

## 第一次准备

推荐使用 Homebrew 安装 Python：

```bash
brew install python
```

然后在仓库根目录执行：

```bash
python3 -m venv .venv
. .venv/bin/activate
python -m pip install -r mac/requirements.txt
```

也可以直接使用一键脚本，它会自动创建 `.venv` 并安装 `pyserial`：

```bash
chmod +x mac/run_mac_collector.sh
./mac/run_mac_collector.sh --port /dev/cu.usbserial-XXXX
```

## 采集步骤

1. 先把 C2C 线接到车机和 Mac。
2. 查看串口名称：

   ```bash
   ls -l /dev/cu.*
   ```

   常见名称包含 `cu.usbserial`、`cu.wchusbserial` 或 `cu.usbmodem`。优先使用 CH342 的 B 通道；如果不确定，可以运行下面的命令让程序列出候选：

   ```bash
   ./mac/run_mac_collector.sh
   ```

3. 先启动采集器；看到“先开始录制，再等待设备上电”后，再给车机上电。
4. 采集完成后，把输出的 `ThorEvidence-Capture-*.zip` 整个发回。

示例：

```bash
./mac/run_mac_collector.sh --port /dev/cu.usbserial-ABC123 --duration 90
```

程序不会发送 `poweron`、复位、刷写或 CAN 命令，只发送 `version`、`help`、电压/状态/温度等固定只读命令。
## 无设备测试

```bash
python mac/thor_nvshell_collect.py --self-test
python mac/thor_nvshell_collect.py --replay fixtures/synthetic-boot.log --output mac/test-output
```

`replay` 输出是 synthetic replay，不能当作真实硬件证据。
