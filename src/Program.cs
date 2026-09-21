using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ThorEvidence.Core;

namespace ThorEvidenceCollector
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string outputRoot = AppDomain.CurrentDomain.BaseDirectory;
            string replayPath = null;
            bool demo = false;
            bool selfTest = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? "";
                if (String.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                else if (String.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                else if (String.Equals(arg, "--replay", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) replayPath = args[++i];
                else if (String.Equals(arg, "--output-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outputRoot = args[++i];
            }

            if (selfTest)
            {
                StringWriter writer = new StringWriter(new StringBuilder(), System.Globalization.CultureInfo.InvariantCulture);
                int code = SelfTestRunner.Run(writer);
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt"), writer.ToString(), new UTF8Encoding(false)); } catch { }
                return code;
            }

            if (!String.IsNullOrEmpty(replayPath))
            {
                try
                {
                    string zip = ReplayRunner.Run(replayPath, Path.Combine(outputRoot, "Evidence"));
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-replay-result.txt"), zip, new UTF8Encoding(false)); } catch { }
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "replay-error.txt"), ex.ToString(), new UTF8Encoding(false)); } catch { }
                    return 2;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(demo, outputRoot));
            return 0;
        }
    }
}
