using System;
using System.IO;

namespace ThorEvidence.Core
{
    public static class SessionPaths
    {
        public static string CreateSessionDirectory(string outputRoot, string prefix)
        {
            string root = Path.GetFullPath(String.IsNullOrEmpty(outputRoot) ? "Evidence" : outputRoot);
            Directory.CreateDirectory(root);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string baseName = String.IsNullOrEmpty(prefix) ? stamp : prefix + "-" + stamp;
            string path = Path.Combine(root, baseName);
            int suffix = 2;
            while (Directory.Exists(path) || File.Exists(path))
            {
                path = Path.Combine(root, baseName + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
                suffix++;
            }
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static class ReplayRunner
    {
        public static string Run(string inputPath, string outputRoot)
        {
            if (String.IsNullOrEmpty(inputPath)) throw new ArgumentException("Replay input is required.", "inputPath");
            string fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Replay input was not found.", fullPath);
            // A live Thor monitor may still be appending to the source log. Share
            // read/write/delete so replay remains useful without stopping it.
            byte[] bytes;
            using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > Int32.MaxValue) throw new IOException("Replay input is too large for the portable collector.");
                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                if (offset != bytes.Length) Array.Resize(ref bytes, offset);
            }
            string directory = SessionPaths.CreateSessionDirectory(outputRoot, "Replay");
            CaptureSession session = new CaptureSession(delegate(byte[] ignored) { return false; }, true);
            session.Start(directory, "REPLAY", "synthetic replay input", 0);
            session.ProcessReceived(bytes);
            session.StopAndExport("synthetic replay complete; no serial port was opened");
            session.Dispose();
            return session.LastExportPath;
        }
    }
}
