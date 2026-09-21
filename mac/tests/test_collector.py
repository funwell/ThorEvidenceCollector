import importlib.util
import json
import pathlib
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("thor_nvshell_collect", ROOT / "thor_nvshell_collect.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CollectorCoreTests(unittest.TestCase):
    def test_read_only_policy(self):
        self.assertTrue(MODULE.is_allowed_command("version"))
        self.assertTrue(MODULE.is_allowed_command("pmrunstate"))
        self.assertTrue(MODULE.is_allowed_command("swtstatusdata"))
        for command in ("poweron", "poweronIST", "tegrapoweron", "zkrmcu", "version;poweron", ""):
            self.assertFalse(MODULE.is_allowed_command(command), command)

    def test_parser_handles_fragmented_input(self):
        parser = MODULE.EvidenceParser()
        parser.feed("[mcu_version:ZRD.MCU.TEST][build_date:demo]\r\nNvShell Initial")
        parser.feed("ization Start\r\nNvShell>\r\nSENSE_ACC1 = 0.000 V\r\n")
        parser.feed("SENSE_SOC_PPVCC_UFS = 0.001 V\r\nPM_StateM:INIT->STANDBY\r\n")
        parser.feed("PM_SleepMonitor:Cur wake-up src:0\r\nMCU_PLTFPWRMGR_REQ_POWERDOWN\r\n")
        result = parser.snapshot()
        self.assertEqual(result["mcuVersion"], "ZRD.MCU.TEST")
        self.assertEqual(result["acc1"], "0.000")
        self.assertEqual(result["socUfs"], "0.001")
        self.assertEqual(result["wakeSource"], "0")
        self.assertTrue(result["powerDownRequested"])
        self.assertEqual(result["pmTransitions"], ["INIT->STANDBY"])

    def test_json_summary_is_valid_and_escaped(self):
        parser = MODULE.EvidenceParser()
        parser.feed('[mcu_version:TEST"FW]\r\n')
        with tempfile.TemporaryDirectory() as temp:
            path = MODULE.write_replay_export(
                pathlib.Path(temp),
                parser.snapshot(),
                "REPLAY",
                "synthetic",
                0,
                ["version", "help"],
                b"raw",
                "synthetic test",
            )
            self.assertTrue(path.exists())
            summary = json.loads((path / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(summary["mcuVersion"], 'TEST"FW')
            self.assertTrue((path / "manifest.sha256").exists())


if __name__ == "__main__":
    unittest.main()
