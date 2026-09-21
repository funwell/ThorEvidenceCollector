using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ThorEvidence.Core
{
    public static class EvidenceExporter
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string Export(CaptureSession session, bool partial, string reason)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (String.IsNullOrEmpty(session.SessionDirectory)) throw new InvalidOperationException("Session directory is not initialized.");

            EvidenceSnapshot snapshot = session.Snapshot();
            string summaryTextPath = Path.Combine(session.SessionDirectory, "summary.txt");
            string readmePath = Path.Combine(session.SessionDirectory, "README.txt");
            string summaryJsonPath = Path.Combine(session.SessionDirectory, "summary.json");

            File.WriteAllText(readmePath, BuildReadme(session, partial, reason), Utf8);
            File.WriteAllText(summaryTextPath, EvidenceFormatter.BuildSummary(snapshot, session.PortName, session.PortCaption, session.Synthetic, CommandPolicy.AllowedCommands), Utf8);

            Dictionary<string, string> preSummaryHashes = CollectHashes(session.SessionDirectory, new string[] { "summary.json", "manifest.sha256" });
            File.WriteAllText(summaryJsonPath, BuildFullJson(session, snapshot, partial, reason, preSummaryHashes), Utf8);

            string manifestPath = WriteManifest(session.SessionDirectory);
            string zipPath = CreateZip(session.SessionDirectory);
            return zipPath;
        }

        public static string WriteManifest(string directory)
        {
            if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            Dictionary<string, string> hashes = CollectHashes(directory, new string[] { "manifest.sha256" });
            string path = Path.Combine(directory, "manifest.sha256");
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> item in hashes.OrderBy(delegate(KeyValuePair<string, string> p) { return p.Key; }, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(item.Value);
                builder.Append("  ");
                builder.Append(item.Key.Replace('\\', '/'));
                builder.AppendLine();
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string Sha256File(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static Dictionary<string, string> CollectHashes(string directory, string[] excludedNames)
        {
            HashSet<string> excluded = new HashSet<string>(excludedNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
                string name = Path.GetFileName(files[i]);
                if (excluded.Contains(name)) continue;
                result[relative] = Sha256File(files[i]);
            }
            return result;
        }

        private static string BuildFullJson(CaptureSession session, EvidenceSnapshot snapshot, bool partial, string reason, Dictionary<string, string> hashes)
        {
            StringBuilder builder = new StringBuilder();
            bool first = true;
            builder.Append('{');
            JsonUtil.Property(builder, ref first, "schemaVersion", JsonUtil.StringValue("1.0"));
            JsonUtil.Property(builder, ref first, "synthetic", JsonUtil.BoolValue(session.Synthetic));
            JsonUtil.Property(builder, ref first, "partial", JsonUtil.BoolValue(partial));
            JsonUtil.Property(builder, ref first, "reason", JsonUtil.StringValue(reason));
            JsonUtil.Property(builder, ref first, "startedUtc", JsonUtil.StringValue(session.StartedUtc.ToString("o")));
            JsonUtil.Property(builder, ref first, "endedUtc", JsonUtil.StringValue(DateTime.UtcNow.ToString("o")));
            JsonUtil.Property(builder, ref first, "port", JsonUtil.StringValue(session.PortName));
            JsonUtil.Property(builder, ref first, "caption", JsonUtil.StringValue(session.PortCaption));
            JsonUtil.Property(builder, ref first, "baud", JsonUtil.IntValue(session.BaudRate));
            JsonUtil.Property(builder, ref first, "rawBytes", JsonUtil.IntValue(session.RawBytes));
            EvidenceFormatter.AddSnapshotProperties(builder, ref first, snapshot);
            JsonUtil.Property(builder, ref first, "commandsAllowed", JsonUtil.StringArray(CommandPolicy.AllowedCommands));
            JsonUtil.Property(builder, ref first, "commandsAttempted", JsonUtil.StringArray(snapshot.CommandsAttempted));
            JsonUtil.Property(builder, ref first, "commandsTimedOut", JsonUtil.StringArray(snapshot.CommandsTimedOut));
            JsonUtil.Property(builder, ref first, "warnings", JsonUtil.StringArray(snapshot.Warnings));
            JsonUtil.Property(builder, ref first, "events", JsonUtil.StringArray(snapshot.Events));
            StringBuilder hashObject = new StringBuilder();
            hashObject.Append('{');
            bool hashFirst = true;
            foreach (KeyValuePair<string, string> item in hashes.OrderBy(delegate(KeyValuePair<string, string> p) { return p.Key; }, StringComparer.OrdinalIgnoreCase))
            {
                if (!hashFirst) hashObject.Append(',');
                hashFirst = false;
                hashObject.Append(JsonUtil.StringValue(item.Key));
                hashObject.Append(':');
                hashObject.Append(JsonUtil.StringValue(item.Value));
            }
            hashObject.Append('}');
            JsonUtil.Property(builder, ref first, "fileHashesBeforeSummary", hashObject.ToString());
            JsonUtil.Property(builder, ref first, "safetyPolicy", JsonUtil.StringValue("Only CRLF and fixed read-only commands are transmitted; no poweron, reset, flashing, CAN, or free-form input."));
            builder.Append('}');
            return builder.ToString();
        }

        private static string BuildReadme(CaptureSession session, bool partial, string reason)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("ThorEvidenceCollector evidence bundle");
            builder.AppendLine("====================================");
            builder.AppendLine("This bundle contains serial evidence only.");
            builder.AppendLine("synthetic=" + session.Synthetic.ToString().ToLowerInvariant());
            builder.AppendLine("partial=" + partial.ToString().ToLowerInvariant());
            builder.AppendLine("reason=" + (reason ?? ""));
            builder.AppendLine("port=" + (session.PortName ?? ""));
            builder.AppendLine("caption=" + (session.PortCaption ?? ""));
            builder.AppendLine("baud=" + session.BaudRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 8N1 flow=none");
            builder.AppendLine();
            builder.AppendLine("Safety policy:");
            builder.AppendLine("- The collector has no free-form command input.");
            builder.AppendLine("- It transmits only one safe CRLF prompt wake and these exact commands:");
            builder.AppendLine("  version");
            builder.AppendLine("  help");
            builder.AppendLine("  showvoltages");
            builder.AppendLine("  pmstateget");
            builder.AppendLine("  readtemp");
            builder.AppendLine("- It never transmits poweron, poweroff, reset, recovery, flashing, CAN, or unknown commands.");
            builder.AppendLine("- DTR and RTS are disabled; no break or automatic port switching is performed.");
            builder.AppendLine();
            builder.AppendLine("Files:");
            builder.AppendLine("- raw-rx.bin: exact bytes received from the selected port.");
            builder.AppendLine("- serial-transcript.log: timestamped human-readable RX/TX transcript.");
            builder.AppendLine("- commands/: separate read-only command outputs.");
            builder.AppendLine("- summary.json and summary.txt: normalized evidence and warnings.");
            builder.AppendLine("- manifest.sha256: SHA-256 for every file except the manifest itself.");
            builder.AppendLine();
            builder.AppendLine("Synthetic replay/demo data must not be presented as a physical serial capture.");
            return builder.ToString();
        }

        private static string CreateZip(string sessionDirectory)
        {
            string parent = Directory.GetParent(sessionDirectory).FullName;
            string baseName = "ThorEvidence-" + Path.GetFileName(sessionDirectory);
            string zipPath = Path.Combine(parent, baseName + ".zip");
            int suffix = 2;
            while (File.Exists(zipPath))
            {
                zipPath = Path.Combine(parent, baseName + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".zip");
                suffix++;
            }
            ZipFile.CreateFromDirectory(sessionDirectory, zipPath, CompressionLevel.Fastest, false);
            return zipPath;
        }
    }
}
