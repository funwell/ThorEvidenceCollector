ThorEvidenceCollector
====================

这是给群友使用的 Thor NvShell 证据采集器。双击 ThorEvidenceCollector.exe，选择明确的 B 通道并点击“开始采集”。程序会同时尝试打开 A 通道做完全被动监听；它会先开始保存原始 RX，再等待设备上电，不会自动发送 poweron。

安全边界
--------
程序没有自由命令输入，只允许发送以下精确只读命令：

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

除提示所需的一次 CRLF 外，不发送 poweron、复位、recovery、zkrmcu、刷写、CAN 或未知命令。DTR/RTS 关闭，不发送 break，不会关闭或杀死其他串口程序，也不会枚举后自动尝试其他端口。

导出内容
--------
每次会在 Evidence\yyyyMMdd-HHmmss\ 下生成 raw-rx.bin、serial-transcript.log、summary.json、summary.txt、commands\、README.txt、manifest.sha256，并在同级生成 ThorEvidence-*.zip。raw-rx.bin 是收到的原始字节；GUI 可复制摘要并打开目录。

验证方式
--------
--self-test 执行内置安全和导出自检。
--replay <file> 在不打开串口的情况下解析已有日志，并标记为 synthetic replay。
--demo 打开 GUI，注入合成分片启动流并自动导出；DEMO 不能当作真实车机证据。

真实硬件使用时，请群友选择自己确认的 CH342 B 通道；A 通道由程序自动打开并只读监听。保留完整 ZIP，不要把合成 DEMO 或 replay 文件当作物理串口采集。
