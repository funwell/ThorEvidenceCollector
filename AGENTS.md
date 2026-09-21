# Agent Instructions

本仓库是 Thor NvShell 证据采集器。修改前先阅读 `README.md` 和 `DESIGN.md`。

- 只做只读采集；不得新增任意命令输入或发送电源/复位/刷写/CAN 命令。
- 所有解析器和导出行为先补测试，再实现。
- 不提交 `dist/`、EXE、ZIP、真实日志、真实设备原始字节或包含本机路径的回放产物。
- 真实串口未连接时只能使用 `--replay` 和 `--demo`。
- 完成前运行 `test.ps1`、`build.ps1` 和 `package.ps1`，并检查导出内容和残留进程。
- 不使用破坏性 Git 操作，不修改 `ThorNvShellMonitor` 旁边的其他项目。
