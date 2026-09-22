using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ThorEvidence.Core;

internal static class CollectorCoreTests
{
    private static int failures;

    private static void AssertTrue(bool value, string name)
    {
        if (!value)
        {
            failures++;
            Console.Error.WriteLine("FAIL: " + name);
        }
        else Console.WriteLine("PASS: " + name);
    }

    private static void AssertEqual(string expected, string actual, string name)
    {
        if (!String.Equals(expected, actual, StringComparison.Ordinal))
        {
            failures++;
            Console.Error.WriteLine("FAIL: " + name + " expected=[" + expected + "] actual=[" + actual + "]");
        }
        else Console.WriteLine("PASS: " + name);
    }

    private static string TempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TestReadOnlyPolicy()
    {
        string[] allowed =
        {
            "version", "help", "showvoltages", "pmstateget", "readtemp",
            "pmrunstate", "pncstatus", "socstatus", "readvolt", "readvrs12",
            "swtlinkstatus", "swtSqiValue", "swtCrcCount", "swtstatusdata"
        };
        string[] denied =
        {
            "poweron", "poweronIST", "tegrapoweron", "poweroff", "poweroffIST", "tegrareset",
            "tegrarecovery", "aurixreset", "zkrmcu", "zkrcfg", "cycliccanon", "pnc29",
            "voltageMonitor disable", "version;poweron", "version\r\n", "version extra", ""
        };

        foreach (string command in allowed) AssertTrue(CommandPolicy.IsAllowed(command), "allow " + command);
        foreach (string command in denied) AssertTrue(!CommandPolicy.IsAllowed(command), "deny " + command);
        AssertEqual("version, help, showvoltages, pmstateget, readtemp, pmrunstate, pncstatus, socstatus, readvolt, readvrs12, swtlinkstatus, swtSqiValue, swtCrcCount, swtstatusdata", String.Join(", ", CommandPolicy.AllowedCommands), "fixed allowlist order");
        AssertEqual("version\r\n", CommandPolicy.ToWireCommand("version"), "wire format");
    }

    private static void TestParserAcrossChunksAndRepeatedBoots()
    {
        EvidenceParser parser = new EvidenceParser();
        parser.Append("[init][warn][osm]:[mcu_version:ZRD.MCU.V4.0-H7-TU.G30.2026.9.b771.2241390423111005]");
        parser.Append("[build_date:15:00:00, Apr 23 2026]\r\n");
        parser.Append("******** NvShell Initialization Start ********\r\nPress 'Enter' for NvShell prompt\r\nNvShe");
        parser.Append("ll> SENSE_KL30_VBAT = 12.184 V\r\nSENSE_VDD_12V = 12.106 V\r\nSENSE_ACC1 = 0.000 V\r\n");
        parser.Append("SENSE_SERDES_1V8 = 0.001 V\r\nSENSE_SOC_PPVCC_UFS = 0.001 V\r\nPG_SOC_VRS11 = 0.009 V\r\n");
        parser.Append("[warn][hwpm]:PM_StateM:INIT->STANDBY\r\n[warn][hwpm]:PM_SleepMonitor:Cur wake-up src:0\r\n");
        parser.Append("ERROR: MCU_PLTFPWRMGR_REQ_POWERDOWN\r\n[warn][hwpm]:PM_StateM:STANDBY->SLEEP\r\n");
        parser.Append("NvShell Initialization Start\r\nNvShell>\r\nPM_StateM:INIT->STANDBY\r\n");
        parser.Append("binary replacement: \ufffd\r\n");

        EvidenceSnapshot snapshot = parser.Snapshot();
        AssertEqual("ZRD.MCU.V4.0-H7-TU.G30.2026.9.b771.2241390423111005", snapshot.McuVersion, "mcu version");
        AssertEqual("15:00:00, Apr 23 2026", snapshot.BuildDate, "build date");
        AssertEqual("12.184", snapshot.Kl30Vbat, "KL30");
        AssertEqual("12.106", snapshot.Vdd12, "VDD12");
        AssertEqual("0.000", snapshot.Acc1, "ACC1");
        AssertEqual("0.001", snapshot.Serdes18, "SERDES 1V8");
        AssertEqual("0.001", snapshot.SocUfs, "SOC UFS");
        AssertEqual("0.009", snapshot.PgSocVrs11, "PG SOC VRS11");
        AssertEqual("0", snapshot.WakeSource, "wake source");
        AssertTrue(snapshot.NvShellSeen, "NvShell detected across chunks");
        AssertTrue(snapshot.PromptSeen, "prompt detected across chunks");
        AssertTrue(snapshot.PowerDownRequested, "powerdown request detected");
        AssertEqual("INIT->STANDBY | STANDBY->SLEEP", String.Join(" | ", snapshot.PowerTransitions.ToArray()), "unique state transitions");
        AssertEqual("2", snapshot.BootCycleCount.ToString(), "repeated boot cycles");
        AssertTrue(snapshot.Warnings.Contains("ACC1 is 0 V in at least one sample"), "generated ACC1 warning");
    }

    private static void TestSummaryAndJsonEscape()
    {
        EvidenceParser parser = new EvidenceParser();
        parser.Append("[mcu_version:TEST\\\"FW]\r\nSENSE_ACC1 = 0.000 V\r\nPM_StateM:INIT->STANDBY\r\n");
        EvidenceSnapshot snapshot = parser.Snapshot();
        string summary = EvidenceFormatter.BuildSummary(snapshot, "COM6", "USB-Enhanced-SERIAL-B", true, CommandPolicy.AllowedCommands);
        string json = EvidenceFormatter.BuildJson(snapshot, "COM6", "caption \" and \\", true, new string[] { "version", "help" });
        AssertTrue(summary.IndexOf("TEST\\\"FW", StringComparison.Ordinal) >= 0, "summary includes version");
        AssertTrue(summary.IndexOf("仅发送只读命令", StringComparison.Ordinal) >= 0, "summary safety statement");
        AssertTrue(json.IndexOf("TEST\\\\\\\"FW", StringComparison.Ordinal) >= 0, "json escapes version");
        AssertTrue(json.IndexOf("caption \\\" and \\\\", StringComparison.Ordinal) >= 0, "json escapes caption");
        AssertTrue(json.IndexOf("\"bootBannerSeen\":false", StringComparison.Ordinal) >= 0, "json boot flag false");
    }

    private static void TestCaptureTimeoutAndTransmitPolicy()
    {
        string directory = TempDirectory("ThorEvidenceCaptureTest");
        List<string> transmissions = new List<string>();
        CaptureSession session = new CaptureSession(delegate(byte[] bytes)
        {
            transmissions.Add(Encoding.ASCII.GetString(bytes));
            return true;
        }, true);
        try
        {
            session.Start(directory, "TEST", "synthetic", 115200);
            session.ProcessReceived(Encoding.UTF8.GetBytes("NvShell Initialization Start\r\nPress 'Enter' for NvShell prompt\r\nNvShell>\r\n"));
            session.Tick(DateTime.UtcNow.AddSeconds(1));
            session.Tick(DateTime.UtcNow.AddSeconds(8));
            EvidenceSnapshot snapshot = session.Snapshot();
            AssertTrue(snapshot.CommandsAttempted.Contains("version"), "timeout command attempted");
            AssertTrue(snapshot.CommandsTimedOut.Contains("version"), "timeout bookkeeping");
            AssertTrue(File.Exists(Path.Combine(directory, "commands", "001-version.txt")), "timeout command file");
            AssertTrue(transmissions.Count >= 2, "safe CRLF and read-only transmit");
            for (int i = 0; i < transmissions.Count; i++)
            {
                string tx = transmissions[i];
                bool safe = tx == "\r\n" || CommandPolicy.IsAllowed(tx.Trim('\r', '\n'));
                AssertTrue(safe, "TX allowlist item " + i.ToString());
                AssertTrue(tx.IndexOf("poweron", StringComparison.OrdinalIgnoreCase) < 0, "TX has no poweron " + i.ToString());
            }
            session.StopAndExport("test stop");
            AssertTrue(session.IsExported, "partial capture exported");
            AssertTrue(File.Exists(session.LastExportPath), "capture zip exists");
        }
        finally
        {
            session.Dispose();
            try { Directory.Delete(directory, true); } catch { }
            string evidenceParent = Directory.GetParent(directory).FullName;
            foreach (string zip in Directory.GetFiles(evidenceParent, "ThorEvidence-" + Path.GetFileName(directory) + "*.zip")) try { File.Delete(zip); } catch { }
        }
    }

    private static void TestNoPromptDoesNotTransmit()
    {
        string directory = TempDirectory("ThorEvidenceNoPrompt");
        int txCount = 0;
        CaptureSession session = new CaptureSession(delegate(byte[] bytes) { txCount++; return true; }, true);
        try
        {
            session.Start(directory, "TEST", "synthetic", 115200);
            session.ProcessReceived(new byte[] { 0, 255, 13, 10, 65, 66 });
            AssertTrue(txCount == 0, "no prompt means no transmit");
            AssertTrue(session.Snapshot().Warnings.Contains("prompt not captured"), "no prompt warning");
            session.StopAndExport("no prompt test");
        }
        finally
        {
            session.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void TestLargeReplayChunkDoesNotOverflowDecoder()
    {
        string directory = TempDirectory("ThorEvidenceLargeReplay");
        CaptureSession session = new CaptureSession(delegate(byte[] bytes) { return true; }, true);
        try
        {
            session.Start(directory, "REPLAY", "synthetic large input", 0);
            string prefix = "NvShell Initialization Start\r\nNvShell>\r\n";
            string suffix = "\r\nSENSE_ACC1 = 0.000 V\r\nPM_StateM:STANDBY->SLEEP\r\n";
            string body = new String('A', 180000);
            byte[] input = Encoding.UTF8.GetBytes(prefix + body + suffix);
            session.ProcessReceived(input);
            AssertTrue(session.RawBytes == input.Length, "large replay raw byte count");
            AssertEqual("0.000", session.Snapshot().Acc1, "large replay parser result");
            session.StopAndExport("large replay test");
            AssertTrue(session.IsExported, "large replay exported");
        }
        finally
        {
            session.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void TestEmptyInputUnsupportedOutputAndPortErrors()
    {
        EvidenceParser empty = new EvidenceParser();
        empty.Append("");
        EvidenceSnapshot emptySnapshot = empty.Snapshot();
        AssertTrue(!emptySnapshot.NvShellSeen, "empty input has no NvShell");
        AssertTrue(emptySnapshot.Warnings.Contains("prompt not captured"), "empty input warning");

        EvidenceParser unsupported = new EvidenceParser();
        unsupported.Append("Unknown command: poweron\r\n");
        EvidenceSnapshot unsupportedSnapshot = unsupported.Snapshot();
        AssertTrue(unsupportedSnapshot.CommandsAttempted.Count == 0, "unsupported output is not an attempted transmit");
        AssertTrue(!CommandPolicy.IsAllowed("Unknown command: poweron"), "unsupported command remains denied");

        PortDiscoveryResult noPorts = new PortDiscoveryResult();
        AssertTrue(PortDiscovery.Find(noPorts, "COM999") == null, "missing port selection is rejected");

        string directory = TempDirectory("Thor evidence nonascii ");
        CaptureSession locked = new CaptureSession(delegate(byte[] bytes)
        {
            throw new IOException("port already in use");
        }, true);
        try
        {
            locked.Start(directory, "COM_LOCKED", "synthetic", 115200);
            locked.ProcessReceived(Encoding.UTF8.GetBytes("NvShell Initialization Start\r\nNvShell>\r\n"));
            AssertTrue(locked.Snapshot().Warnings.Contains("serial transmit exception: port already in use"), "port-in-use diagnostic");
            locked.StopAndExport("port-in-use test");
        }
        finally
        {
            locked.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }

        try
        {
            EvidenceExporter.WriteManifest(Path.Combine(Path.GetTempPath(), "missing-Thor-evidence-" + Guid.NewGuid().ToString("N")));
            AssertTrue(false, "export failure is surfaced");
        }
        catch (DirectoryNotFoundException)
        {
            AssertTrue(true, "export failure is surfaced");
        }
    }

    private static void TestHashManifest()
    {
        string directory = TempDirectory("ThorEvidenceHash");
        try
        {
            File.WriteAllText(Path.Combine(directory, "a.txt"), "hash test", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "b 空间.txt"), "unicode path", new UTF8Encoding(false));
            string manifest = EvidenceExporter.WriteManifest(directory);
            string content = File.ReadAllText(manifest, Encoding.UTF8);
            AssertTrue(content.IndexOf("a.txt", StringComparison.Ordinal) >= 0, "manifest lists a.txt");
            AssertTrue(content.IndexOf("b 空间.txt", StringComparison.Ordinal) >= 0, "manifest lists non-ASCII path");
            AssertTrue(content.IndexOf("manifest.sha256", StringComparison.OrdinalIgnoreCase) < 0, "manifest excludes itself");
            AssertEqual(EvidenceExporter.Sha256File(Path.Combine(directory, "a.txt")), content.Split(new string[] { "  a.txt" }, StringSplitOptions.None)[0].Trim(), "manifest hash matches");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void TestPassiveSerialCaptureNeverTransmits()
    {
        string directory = TempDirectory("ThorEvidencePassive");
        PassiveSerialCapture passive = new PassiveSerialCapture();
        try
        {
            passive.Start(directory, "COM5", "USB-Enhanced-SERIAL-A CH342", 115200);
            passive.ProcessReceived(Encoding.ASCII.GetBytes("Linux version synthetic\r\n"));
            passive.Stop();
            AssertTrue(File.Exists(Path.Combine(directory, "serial-A-rx.bin")), "passive raw file");
            AssertTrue(File.Exists(Path.Combine(directory, "serial-A-transcript.log")), "passive transcript");
            AssertTrue(File.Exists(Path.Combine(directory, "serial-A-summary.json")), "passive summary");
            string transcript = File.ReadAllText(Path.Combine(directory, "serial-A-transcript.log"), Encoding.UTF8);
            AssertTrue(transcript.IndexOf("[TX]", StringComparison.OrdinalIgnoreCase) < 0, "passive transcript has no TX");
            AssertTrue(passive.RawBytes == 25, "passive raw bytes");
        }
        finally
        {
            passive.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    public static int Main()
    {
        TestReadOnlyPolicy();
        TestParserAcrossChunksAndRepeatedBoots();
        TestSummaryAndJsonEscape();
        TestCaptureTimeoutAndTransmitPolicy();
        TestNoPromptDoesNotTransmit();
        TestLargeReplayChunkDoesNotOverflowDecoder();
        TestEmptyInputUnsupportedOutputAndPortErrors();
        TestHashManifest();
        TestPassiveSerialCaptureNeverTransmits();
        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
        return failures == 0 ? 0 : 1;
    }
}
