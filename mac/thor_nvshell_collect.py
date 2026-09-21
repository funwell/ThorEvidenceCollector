#!/usr/bin/env python3
"""Cross-platform, read-only Thor NvShell evidence collector for macOS/Linux."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


SAFE_COMMANDS: Tuple[str, ...] = (
    "version",
    "help",
    "showvoltages",
    "pmstateget",
    "readtemp",
    "pmrunstate",
    "pncstatus",
    "socstatus",
    "readvolt",
    "readvrs12",
    "swtlinkstatus",
    "swtSqiValue",
    "swtCrcCount",
    "swtstatusdata",
)


def is_allowed_command(command: str) -> bool:
    return isinstance(command, str) and command in SAFE_COMMANDS


def wire_command(command: str) -> bytes:
    if not is_allowed_command(command):
        raise ValueError("command rejected by the read-only allowlist")
    return (command + "\r\n").encode("ascii")


def now_local() -> str:
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def now_utc() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


class EvidenceParser:
    def __init__(self) -> None:
        self.text = ""

    def feed(self, text: str) -> None:
        self.text += text or ""
        if len(self.text) > 4_000_000:
            self.text = self.text[-3_000_000:]

    def _last(self, pattern: str, flags: int = re.IGNORECASE) -> str:
        matches = list(re.finditer(pattern, self.text, flags))
        return matches[-1].group(1).strip() if matches else ""

    def _has(self, pattern: str) -> bool:
        return re.search(pattern, self.text, re.IGNORECASE | re.DOTALL) is not None

    def snapshot(self) -> Dict[str, object]:
        transitions: List[str] = []
        for match in re.finditer(r"PM_StateM:\s*([^\r\n]+)", self.text, re.IGNORECASE):
            value = match.group(1).strip()
            if value.startswith("Cur PMState"):
                continue
            if value not in transitions:
                transitions.append(value)

        warnings: List[str] = []
        acc1 = self._last(r"SENSE_ACC1\s*=\s*([-+]?\d+(?:\.\d+)?)")
        soc_ufs = self._last(r"SENSE_SOC_PPVCC_UFS\s*=\s*([-+]?\d+(?:\.\d+)?)")
        serdes18 = self._last(r"SENSE_SERDES_1V8\s*=\s*([-+]?\d+(?:\.\d+)?)")
        if acc1 and float(acc1) < 0.1:
            warnings.append("ACC1 is 0 V in at least one sample")
        if any(value and float(value) < 0.1 for value in (soc_ufs, serdes18)):
            warnings.append("SoC/SerDes/UFS rails remain near zero")
        if self._has(r"MCU_PLTFPWRMGR_REQ_POWERDOWN"):
            warnings.append("MCU requested powerdown")
        wake = self._last(r"(?:Cur\s+)?wake-up\s+src\s*[:=]\s*([^\s\r\n]+)")
        if wake == "0":
            warnings.append("wake source is zero")
        if not self._has(r"NvShell\s+Initialized|NvShell\s+Initialization\s+Start"):
            warnings.append("NvShell boot banner not captured")
        if not self._has(r"NvShell>"):
            warnings.append("prompt not captured")

        return {
            "mcuVersion": self._last(r"mcu_version:([^\]\r\n]+)"),
            "mcuA": self._last(r"mcuA\s+Version\s+([^\s\r\n]+)"),
            "buildDate": self._last(r"build_date:([^\]\r\n]+)"),
            "defaultBootChain": self._last(r"DefaultBootChain:\s*([^,\s]+)"),
            "nextBootChain": self._last(r"NextBootChain:\s*([^\s]+)"),
            "bomId": self._last(r"BomID:\s*([^\s]+)"),
            "boardBomId": self._last(r"Board_BomID:\s*([^\s]+)"),
            "nvShellSeen": self._has(r"NvShell\s+Initialized|NvShell\s+Initialization\s+Start"),
            "promptSeen": self._has(r"NvShell>"),
            "bootCycleCount": len(re.findall(r"NvShell\s+Initialization\s+Start", self.text, re.IGNORECASE)),
            "kl30Vbat": self._last(r"SENSE_KL30_VBAT\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "vdd12": self._last(r"SENSE_VDD_12V\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "vbatSoc": self._last(r"SENSE_VBAT_SOC\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "acc1": acc1,
            "vrs5": self._last(r"SENSE_VRS_5V_OR_SOCVCC\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "prereg16": self._last(r"SENSE_PREREG_16V\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "prereg5": self._last(r"SENSE_PREREG_5V\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "serdes18": serdes18,
            "serdes12": self._last(r"SENSE_SERDES_1V2\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "serdes10": self._last(r"SENSE_SERDES_1V0\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "socUfs": soc_ufs,
            "pgPwrSerdes": self._last(r"PG_PWR_SERDES\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "pgSocVrs11": self._last(r"PG_SOC_VRS11\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "socPgLcvr": self._last(r"SOC_PG_LCVR\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "socPgLcvrAo": self._last(r"SOC_PG_LCVR_AO\s*=\s*([-+]?\d+(?:\.\d+)?)"),
            "mcuTemp": self._last(r"mcu_temp:\s*([-+]?\d+(?:\.\d+)?)"),
            "socTemp": self._last(r"T_Soc:\s*[-+]?\d+[-:]([-+]?\d+(?:\.\d+)?)"),
            "wakeSource": wake,
            "powerDeviceStatus": self._last(r"PM_PowerMonitor:.*?status[:=]\s*([^\s\r\n]+)"),
            "pmState": self._last(r"PM_StateM:\s*Cur\s+PMState:([^\r\n]+)"),
            "pmTransitions": transitions,
            "powerUpRequested": self._has(r"MCU_PLTFPWRMGR_REQ_POWERUP"),
            "powerDownRequested": self._has(r"MCU_PLTFPWRMGR_REQ_POWERDOWN"),
            "socTempReadFailed": self._has(r"soc.*?(?:get temp fail|temp.*fail)"),
            "dtcSocLink": self._has(r"DTC_SOC\s+LINK"),
            "dtcDhuLink": self._has(r"DTC_DHU\s+LINK"),
            "warnings": warnings,
        }


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_manifest(session_dir: Path) -> Path:
    manifest = session_dir / "manifest.sha256"
    rows: List[str] = []
    for path in sorted(session_dir.rglob("*")):
        if not path.is_file() or path.name == manifest.name:
            continue
        rows.append(f"{sha256_file(path)}  {path.relative_to(session_dir).as_posix()}")
    manifest.write_text("\n".join(rows) + "\n", encoding="utf-8")
    return manifest


def _safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]+", "_", value or "empty")


def write_replay_export(
    output_root: Path,
    snapshot: Dict[str, object],
    port: str,
    caption: str,
    baud: int,
    commands: Sequence[str],
    raw: bytes,
    reason: str,
) -> Path:
    output_root.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    session_dir = output_root / f"Replay-{stamp}"
    suffix = 2
    while session_dir.exists():
        session_dir = output_root / f"Replay-{stamp}-{suffix}"
        suffix += 1
    session_dir.mkdir(parents=True)
    (session_dir / "commands").mkdir()
    (session_dir / "session.json").write_text(
        json.dumps({
            "schemaVersion": 1,
            "synthetic": True,
            "port": port,
            "caption": caption,
            "baud": baud,
            "reason": reason,
            "startedUtc": now_utc(),
        }, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    (session_dir / "raw-rx.bin").write_bytes(raw)
    text = raw.decode("utf-8", errors="replace")
    (session_dir / "serial-transcript.log").write_text(f"{now_local()} [RX] {text}", encoding="utf-8")
    payload = dict(snapshot)
    payload.update({
        "schemaVersion": 1,
        "synthetic": True,
        "partial": False,
        "port": port,
        "caption": caption,
        "baud": baud,
        "commandsAllowed": list(SAFE_COMMANDS),
        "commandsAttempted": [],
        "commandsTimedOut": [],
        "reason": reason,
        "rawBytes": len(raw),
        "startedUtc": now_utc(),
        "endedUtc": now_utc(),
    })
    (session_dir / "summary.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    summary_lines = [
        "Thor NvShell macOS/Linux replay summary",
        "========================================",
        f"Evidence type: synthetic replay",
        f"MCU: {payload.get('mcuVersion', '')}",
        f"ACC1: {payload.get('acc1', '')} V",
        f"SoC UFS: {payload.get('socUfs', '')} V",
        f"Wake source: {payload.get('wakeSource', '')}",
        "Warnings:",
    ]
    summary_lines.extend(f"  - {item}" for item in payload.get("warnings", []))
    summary_lines.append("Safety: only read-only commands are allowed; no power/reset/flash/CAN commands are sent.")
    (session_dir / "summary.txt").write_text("\n".join(summary_lines) + "\n", encoding="utf-8")
    (session_dir / "README.txt").write_text(
        "This is a synthetic replay export. It was produced without opening a serial port.\n",
        encoding="utf-8",
    )
    write_manifest(session_dir)
    zip_path = output_root / f"ThorEvidence-Replay-{stamp}.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(session_dir.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(session_dir).as_posix())
    return session_dir


def list_serial_ports() -> List[Tuple[str, str, int]]:
    try:
        from serial.tools import list_ports
    except ImportError as exc:
        raise RuntimeError("pyserial is required. Run: python3 -m pip install -r mac/requirements.txt") from exc
    ports: List[Tuple[str, str, int]] = []
    for item in list_ports.comports():
        device = item.device
        if not (device.startswith("/dev/cu.") or device.startswith("/dev/tty.")):
            continue
        description = item.description or item.hwid or "serial device"
        upper = description.upper()
        score = 0
        if device.startswith("/dev/cu."):
            score += 10
        if "USB-ENHANCED-SERIAL-B" in upper:
            score += 100
        elif "CH342" in upper and "B" in upper:
            score += 90
        elif "WCH" in upper or "CH34" in upper:
            score += 50
        ports.append((device, description, score))
    return sorted(ports, key=lambda row: (-row[2], row[0]))


class SerialCollector:
    def __init__(self, port: str, caption: str, output_root: Path, baud: int = 115200, duration: int = 90) -> None:
        self.port = port
        self.caption = caption
        self.output_root = output_root
        self.baud = baud
        self.duration = duration
        self.parser = EvidenceParser()
        self.raw = bytearray()
        self.transcript: List[str] = []
        self.events: List[str] = []
        self.attempted: List[str] = []
        self.timed_out: List[str] = []
        self.pending: List[str] = []
        self.active_command: Optional[str] = None
        self.active_output: List[str] = []
        self.active_deadline = 0.0
        self.next_command_at = 0.0
        self.crlf_sent = False
        self.queue_armed = False
        self.session_dir: Optional[Path] = None
        self.started = time.monotonic()

    def log(self, direction: str, text: str) -> None:
        self.transcript.append(f"{now_local()} [{direction}] {text}")

    def send(self, serial_port, payload: bytes, label: str) -> None:
        serial_port.write(payload)
        self.log("TX", label)

    def feed(self, serial_port, data: bytes) -> None:
        self.raw.extend(data)
        text = data.decode("utf-8", errors="replace")
        self.parser.feed(text)
        self.log("RX", text)
        if self.active_command:
            self.active_output.append(text)
            if "NvShell>" in "".join(self.active_output):
                self.finish_command(False)
        prompt_hint = re.search(r"Press.{0,120}Enter.{0,120}NvShell\s+prompt|NvShell\s*>", self.parser.text, re.I | re.S)
        if prompt_hint and not self.crlf_sent:
            self.send(serial_port, b"\r\n", "TX [safe CRLF]")
            self.crlf_sent = True
            self.events.append("safe CRLF sent")
        if self.crlf_sent and self.parser.snapshot()["promptSeen"] and not self.queue_armed:
            self.pending = list(SAFE_COMMANDS)
            self.queue_armed = True
            self.next_command_at = time.monotonic()
            self.events.append("read-only queue armed")

    def finish_command(self, timeout: bool) -> None:
        if not self.active_command or not self.session_dir:
            return
        index = len(self.attempted) + 1
        path = self.session_dir / "commands" / f"{index:03d}-{_safe_name(self.active_command)}.txt"
        content = "".join(self.active_output)
        if timeout:
            content += "\n[collector: TIMEOUT after 6 seconds]\n"
            self.timed_out.append(self.active_command)
        path.write_text(content, encoding="utf-8")
        self.attempted.append(self.active_command)
        self.active_command = None
        self.active_output = []
        self.next_command_at = time.monotonic() + 1.2

    def tick(self, serial_port) -> None:
        current = time.monotonic()
        if self.active_command and current >= self.active_deadline:
            self.finish_command(True)
        if not self.active_command and self.pending and current >= self.next_command_at:
            command = self.pending.pop(0)
            self.send(serial_port, wire_command(command), f"TX [read-only] {command}")
            self.active_command = command
            self.active_output = []
            self.active_deadline = current + 6.0

    def run(self) -> Path:
        try:
            import serial
        except ImportError as exc:
            raise RuntimeError("pyserial is required. Run: python3 -m pip install -r mac/requirements.txt") from exc

        stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        evidence_root = self.output_root / "Evidence"
        evidence_root.mkdir(parents=True, exist_ok=True)
        self.session_dir = evidence_root / f"Capture-{stamp}"
        suffix = 2
        while self.session_dir.exists():
            self.session_dir = evidence_root / f"Capture-{stamp}-{suffix}"
            suffix += 1
        self.session_dir.mkdir()
        (self.session_dir / "commands").mkdir()

        serial_port = serial.Serial(
            self.port,
            self.baud,
            timeout=0.1,
            write_timeout=1.0,
            dsrdtr=False,
            rtscts=False,
            xonxoff=False,
        )
        try:
            self.log("INFO", f"SESSION START port={self.port} baud={self.baud} 8N1 flow=none")
            self.log("INFO", "recording started before any transmit; no poweron command will be sent")
            started = time.monotonic()
            while time.monotonic() - started < self.duration:
                data = serial_port.read(8192)
                if data:
                    self.feed(serial_port, data)
                self.tick(serial_port)
                snap = self.parser.snapshot()
                if not self.pending and not self.active_command and self.attempted and snap["powerDownRequested"]:
                    break
            if self.active_command:
                self.finish_command(True)
        except KeyboardInterrupt:
            self.events.append("interrupted by user")
        finally:
            serial_port.close()

        snapshot = self.parser.snapshot()
        snapshot["warnings"] = list(snapshot.get("warnings", []))
        if not snapshot["promptSeen"]:
            snapshot["warnings"].append("prompt not captured") if "prompt not captured" not in snapshot["warnings"] else None
        snapshot.update({
            "schemaVersion": 1,
            "synthetic": False,
            "partial": False,
            "port": self.port,
            "caption": self.caption,
            "baud": self.baud,
            "commandsAllowed": list(SAFE_COMMANDS),
            "commandsAttempted": self.attempted,
            "commandsTimedOut": self.timed_out,
            "events": self.events,
            "rawBytes": len(self.raw),
            "startedUtc": now_utc(),
            "endedUtc": now_utc(),
        })
        (self.session_dir / "session.json").write_text(
            json.dumps({
                "schemaVersion": 1,
                "synthetic": False,
                "port": self.port,
                "caption": self.caption,
                "baud": self.baud,
                "durationSeconds": self.duration,
                "startedUtc": snapshot["startedUtc"],
                "endedUtc": snapshot["endedUtc"],
            }, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        (self.session_dir / "raw-rx.bin").write_bytes(bytes(self.raw))
        (self.session_dir / "serial-transcript.log").write_text("\n".join(self.transcript) + "\n", encoding="utf-8")
        (self.session_dir / "summary.json").write_text(json.dumps(snapshot, ensure_ascii=False, indent=2), encoding="utf-8")
        (self.session_dir / "summary.txt").write_text(
            "Thor NvShell macOS/Linux capture summary\n"
            "========================================\n"
            f"Port: {self.port}\nMCU: {snapshot.get('mcuVersion', '')}\n"
            f"ACC1: {snapshot.get('acc1', '')} V\nSoC UFS: {snapshot.get('socUfs', '')} V\n"
            f"Wake source: {snapshot.get('wakeSource', '')}\n"
            "Warnings:\n" + "".join(f"  - {item}\n" for item in snapshot.get("warnings", [])) +
            "Safety: only read-only commands are allowed; no power/reset/flash/CAN commands are sent.\n",
            encoding="utf-8",
        )
        (self.session_dir / "README.txt").write_text(
            "真实串口采集。raw-rx.bin 是收到的原始字节；请将整个 ZIP 交给分析人员。\n",
            encoding="utf-8",
        )
        write_manifest(self.session_dir)
        zip_path = self.output_root / f"ThorEvidence-Capture-{stamp}.zip"
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(self.session_dir.rglob("*")):
                if path.is_file():
                    archive.write(path, path.relative_to(self.session_dir).as_posix())
        return zip_path


def run_replay(path: Path, output_root: Path) -> Path:
    raw = path.read_bytes()
    parser = EvidenceParser()
    parser.feed(raw.decode("utf-8", errors="replace"))
    session_dir = write_replay_export(output_root / "Evidence", parser.snapshot(), "REPLAY", "synthetic replay", 0, SAFE_COMMANDS, raw, "synthetic replay")
    return next(output_root.joinpath("Evidence").glob("ThorEvidence-Replay-*.zip"))


def self_test() -> int:
    parser = EvidenceParser()
    parser.feed("NvShell Initialization Start\r\nNvShell>\r\nSENSE_ACC1 = 0.000 V\r\n")
    result = parser.snapshot()
    if not is_allowed_command("version") or is_allowed_command("poweron"):
        print("SELF-TEST FAILED: command policy", file=sys.stderr)
        return 1
    if result["acc1"] != "0.000" or not result["promptSeen"]:
        print("SELF-TEST FAILED: parser", file=sys.stderr)
        return 1
    print("MAC SELF-TEST PASSED")
    return 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Safe read-only Thor NvShell evidence collector")
    parser.add_argument("--port", help="serial device, for example /dev/cu.usbserial-XXXX")
    parser.add_argument("--baud", type=int, default=115200)
    parser.add_argument("--duration", type=int, default=90)
    parser.add_argument("--output", type=Path, default=Path("Evidence"))
    parser.add_argument("--replay", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if args.replay:
        zip_path = run_replay(args.replay, args.output)
        print(zip_path)
        return 0

    ports = list_serial_ports()
    if args.port:
        selected = next((item for item in ports if item[0] == args.port), (args.port, "manual selection", 0))
    elif len(ports) == 1:
        selected = ports[0]
    elif ports and ports[0][2] > 0:
        selected = ports[0]
    else:
        print("发现多个/未明确的串口，请使用 --port。", file=sys.stderr)
        for device, description, _score in ports:
            print(f"  {device} - {description}", file=sys.stderr)
        return 2

    print(f"使用串口: {selected[0]} - {selected[1]}")
    print("将先开始录制，再等待设备上电；程序不会发送 poweron。")
    print("请在看到此提示后给设备上电，完成后把生成的 ZIP 发回。")
    zip_path = SerialCollector(selected[0], selected[1], args.output, args.baud, args.duration).run()
    print(f"完成: {zip_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
