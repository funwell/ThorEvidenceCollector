using System;
using System.IO;
using System.Text;

namespace ThorEvidence.Core
{
    public static class SelfTestRunner
    {
        public static int Run(TextWriter output)
        {
            int failures = 0;
            Action<bool, string> check = delegate(bool ok, string name)
            {
                output.WriteLine((ok ? "PASS: " : "FAIL: ") + name);
                if (!ok) failures++;
            };

            check(CommandPolicy.IsAllowed("version"), "allow version");
            check(CommandPolicy.IsAllowed("help"), "allow help");
            check(!CommandPolicy.IsAllowed("poweron"), "deny poweron");
            check(!CommandPolicy.IsAllowed("version;poweron"), "deny separator");

            EvidenceParser parser = new EvidenceParser();
            parser.Append("NvShe");
            parser.Append("ll Initialization Start\r\nNvShell>\r\nSENSE_ACC1 = 0.000 V\r\n");
            parser.Append("PM_StateM:INIT->STANDBY\r\n");
            EvidenceSnapshot snapshot = parser.Snapshot();
            check(snapshot.NvShellSeen, "fragmented NvShell");
            check(snapshot.PromptSeen, "prompt");
            check(snapshot.Acc1 == "0.000", "voltage");
            check(snapshot.PowerTransitions.Count == 1, "state transition");

            string json = EvidenceFormatter.BuildJson(snapshot, "COM6", "caption \"test\"", true, CommandPolicy.AllowedCommands);
            check(json.IndexOf("caption \\\"test\\\"", StringComparison.Ordinal) >= 0, "JSON escaping");

            string temp = Path.Combine(Path.GetTempPath(), "ThorEvidenceSelfTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                File.WriteAllText(Path.Combine(temp, "a.txt"), "hash test", new UTF8Encoding(false));
                string manifest = EvidenceExporter.WriteManifest(temp);
                check(File.Exists(manifest), "manifest exists");
                check(File.ReadAllText(manifest).IndexOf("a.txt", StringComparison.Ordinal) >= 0, "manifest lists file");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }

            output.WriteLine(failures == 0 ? "ALL SELF-TESTS PASSED" : failures + " SELF-TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
